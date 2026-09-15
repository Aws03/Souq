# ADR-0038: the query layer is adopted, and types arrive as checked JSDoc rather than a TypeScript conversion

- **Status:** Accepted (Phase 16, Storefront rebuild), 2026-09-15. Takes the two adoptions [ADR-0037](0037-frontend-server-state-and-types.md) left pending, at the points it named.
- **Date:** 2026-09-15
- **Related modules:** Cross-cutting (the whole frontend)
- **Related ADRs:** completes [ADR-0037](0037-frontend-server-state-and-types.md); the error shape the cache surfaces is [ADR-0017](0017-error-contract.md); the session it must not duplicate is [ADR-0023](0023-sessions-and-credentials.md); the server-priced basket it must never cache as truth is [ADR-0028](0028-basket-and-pricing-pipeline.md); the testing posture it depends on is [ADR-0015](0015-testing-strategy.md)

## Context

[ADR-0037](0037-frontend-server-state-and-types.md) decided *what* and deferred *when*, with two named triggers:

- **The query library** would arrive "at the first screen that is rebuilt or newly written."
- **TypeScript** would be reconsidered when "a CI pipeline exists."

Both have fired. Phase 16 is the storefront rebuild, and by the time this record was written it had already rebuilt or newly written six screens — the account shell, `MyOrders`, `OrderTracking`, the cart page, the confirmation page and the product page — every one of them on the hand-rolled fetch idiom. `.github/workflows/ci.yml` exists and runs the frontend suite. ADR-0037's own "revisit when" clause anticipated exactly this: *"if the rebuilds slip again, re-open this record rather than silently re-defer."* They did not slip; the adoption point simply arrived and had to be taken deliberately.

The other half of the context is what the rebuild found. Two routed pages — `MyOrders.jsx` and `OrderTracking.jsx` — were **zero-byte files**, and had been since Phase 9. They built cleanly, the suite was green, and both routes failed only in a visitor's browser. That is the class of defect a type checker is supposed to make impossible, and it is the strongest argument in this repository for types being *enforced* rather than *available*.

## Problem

1. Six screens have now been rebuilt against the idiom ADR-0037 called a stopgap. Adopt the library, or admit the decision is not real?
2. The TypeScript trigger has fired. A full conversion of ~9,000 lines is weeks of work with nothing a customer can see. Does "the trigger fired" oblige the expensive answer, or is there a cheaper one that catches the same defects?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| When to adopt the query layer | **Now, storefront read paths in one change.** `useCatalog`, product, related products, reviews, my-orders, order, order tracking, confirmation, profile, addresses, categories | New screens only: two idioms coexisting is TD-24's complaint, and the screens already rebuilt would keep the idiom the ADR called temporary. Defer again: the third deferral on a trigger that has fired is a decision by default |
| How far to migrate | **Storefront reads only.** The admin area keeps its idiom until Phase 17 rebuilds it | A whole-frontend migration: a large diff across screens nobody is otherwise touching, which is the "wholesale rewrite" this programme forbids |
| Cache freshness | **`staleTime: 0` globally** — every screen entry revalidates, and the cached copy is painted meanwhile. One exception, the category tree at five minutes, because it changes only by an admin edit | A comfortable global `staleTime`: this is a shop. A price or a stock count presented as current while being minutes old is the failure mode a cache introduces, and [ADR-0028](0028-basket-and-pricing-pipeline.md) already forbids treating the client as the authority |
| Retry policy | **No retry on 4xx, one retry otherwise.** A product that is 404 will stay 404 | The default three retries: three 404s and a delay before the visitor is told the truth |
| Identity change | **`resetQueries()` on every change of user id**, initial mount excepted | `clear()`, which was tried first and is wrong — see Verification |
| Whether keys carry the tenant | **No, and the reason is written down.** The tenant is resolved from the host on the server, and the cache lives in one page load on one origin, so no path exists for two stores to share a client | Adding a tenant segment to every key: ceremony that implies a threat model the deployment does not have, and invites the belief that the *key* is what enforces isolation |
| TypeScript | **`checkJs` on `.js` modules with JSDoc types at the boundaries, enforced in CI.** No file converted, no `.ts` added | A full conversion: weeks, no customer-visible result, and it would consume the rest of this phase. Staying untyped: the trigger fired, and two empty routed files are the argument against |
| How far `checkJs` reaches | **`.js` only, not `.jsx`** — measured, not assumed. Across the whole tree it produced **248** errors, most of them TypeScript inferring a component's prop shape from its first call site and then objecting to every other one: noise, because props are undeclared. Restricted to `.js` it produced **52**, every one real. All 52 are fixed and the check is clean | Running it over `.jsx` too and silencing the noise: a hundred `@ts-nocheck` comments is a disabled check with extra steps. Components join when their props are declared (Phase 17) |

## Decision

1. **TanStack Query 5.102.8 is installed and the storefront read paths are migrated.** `QueryProvider` sits under `AuthProvider` so it can see identity change.
2. **Query keys live in one module** (`frontend/src/app/queryKeys.js`). A key built in two places drifts, and then one of the two is invalidated and the other is not.
3. **Changing user identity resets every query.** Signing out and in as someone else on the same device does not reload the page, so without this the first customer's orders remain in memory — and the second customer's screen would render them before the server was asked anything at all.
4. **The cache is never the authority.** Prices, stock, permissions, coupon validity and module availability remain server decisions ([ADR-0028](0028-basket-and-pricing-pipeline.md)). `staleTime: 0` is the expression of that in configuration.
5. **Type checking is `checkJs` plus JSDoc at the boundaries**, scoped to `.js`, run by `npm run typecheck` and enforced in CI. The API error shape (`ApiError`), the ProblemDetails body, the page-metadata inputs, the structured-data input and the option bags of the pure modules are now declared.
6. **TypeScript conversion is not adopted, and this is the answer to the trigger — not another deferral.** It is reconsidered only if the checked-JSDoc boundary stops catching what it is there to catch, or if a component rebuild makes prop types cheap to add.
7. **A source-invariant test stands in for what types could not catch here.** No type system would have flagged two empty files behind `lazy()`. `moduleInvariants.test.js` asserts that no source file is empty and that every lazily imported module has a default export.

## Consequences

- **Positive:**
  - TD-23 is closed at its named adoption point rather than carried into a fourth phase.
  - The hand-rolled cancellation guard (TD-25) stops being needed on migrated screens; the lint rule that counts the pattern fell from 27 hits to 22 in this change, and the remaining hits are the admin screens Phase 17 will rebuild.
  - Navigating back to a catalog page, or from the confirmation to the order, paints from cache instead of showing a skeleton for data fetched seconds ago. The confirmation and the order page share one key, so the second is free.
  - Contract drift in the API client and the pure modules is now a build failure, at a cost of one tsconfig and about twenty JSDoc blocks.
- **Negative / limits:**
  - About 13 KB gzipped added to the first bundle.
  - Components are not type-checked. The boundary is typed; the screens are not.
  - The admin area keeps two idioms until Phase 17 — a deliberate, time-boxed inconsistency, recorded in TD-24.
  - `staleTime: 0` means more requests than a cache-happy configuration. That is the intended trade in a shop.

## Revisit when

- Phase 17 rebuilds the admin screens: migrate them then, and reconsider whether `.jsx` can join the type check once props are declared.
- A measured performance problem is traced to `staleTime: 0`, which would change the number, not the principle.
- The checked-JSDoc boundary misses a class of defect it should have caught — then the TypeScript question re-opens with evidence rather than with a date.

## Verification

- `frontend/src/app/QueryProvider.test.jsx` asserts that a change of user resets the cache, that signing out does, and that an ordinary re-render does not. **It found a real bug while being written:** `queryClient.clear()` empties the cache but leaves a mounted observer holding the data it already has and never refetching — so the previous customer's orders stayed on screen. `resetQueries()` is used because the test proved `clear()` insufficient; reverting it fails two tests.
- `frontend/src/app/moduleInvariants.test.js` covers Decision 7, verified by planting both faults (an emptied file, a removed default export).
- `npm run typecheck` is clean and runs in CI, so Decision 5 cannot rot silently.
- The 248-vs-52 error counts in **Options considered** were produced by running `tsc --noEmit` with each `include` scope, not estimated.
