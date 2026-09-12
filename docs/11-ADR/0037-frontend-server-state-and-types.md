# ADR-0037: D-19 decided — a query library is the target for server state, adopted screen by screen; TypeScript waits for a pipeline that can enforce it

- **Status:** Accepted (decided in Phase 17), 2026-09-12. Decides **D-19**, which [ADR-0035](0035-white-label-runtime.md) deferred with a trigger. No dependency is installed by this record.
- **Date:** 2026-09-12
- **Related modules:** Cross-cutting (the whole frontend)
- **Related ADRs:** decides the D-19 deferral of [ADR-0035](0035-white-label-runtime.md); the errors a query layer would surface are the contract of [ADR-0017](0017-error-contract.md); the session refresh it must not duplicate is [ADR-0023](0023-sessions-and-credentials.md); the testing posture it depends on is [ADR-0015](0015-testing-strategy.md); the server-priced basket it must never cache as truth is [ADR-0028](0028-basket-and-pricing-pipeline.md)

## Context

[ADR-0035](0035-white-label-runtime.md) deferred D-19 — TypeScript and TanStack Query — with a trigger: "the start of Phase 16, or adding a CI type-check, whichever comes first." Its reasoning was that the storefront and admin rebuilds would rewrite most data-fetching screens anyway, so introducing a query layer there would avoid writing the same screens twice.

The trigger has fired and the premise has partly expired. The rebuilds have **not** happened: this phase is production hardening, not the storefront rebuild. Meanwhile the debt register carries the consequence as TD-23 at P1 ("every new screen is built twice"), and the risk register carries it as R-28. Deferring a third time on reasoning the repository no longer satisfies would be a decision by default.

So the state of the frontend was measured rather than recalled. Counted across `frontend/src` during Phase 17:

| Measure | Count |
|---|---|
| Components that fetch inside an effect | **29** |
| …that guard against a superseded response | **5** (the catalog hook, the storefront page, the tenant provider, the auth context, the wishlist context) |
| …that do **not** | **24** |
| Hand-rolled `loading` / `error` / `busy` state declarations | **80** |
| `useState` occurrences overall | **258** |
| Callback plumbing that exists only to refetch a parent (`onRetry`, `onChanged`, `onSaved`, …) | about **30** |
| Named endpoint functions in the single API client | **92**, imported by **36** modules |
| TypeScript files, PropTypes declarations, `tsconfig` | **0**, **0**, none |
| Frontend test files | **23**, every one covering pure logic |
| First bundle | 367 KB raw, **121.6 KB gzipped** |

The debt register described the cancellation gap as "half the data-fetching effects". It is **24 of 29** — 83%.

## Problem

Two questions, and they have different answers, which is why D-19 as a single bundled decision resisted being settled for two phases.

1. **Server state.** Twenty-nine screens have each re-implemented fetching, loading, error and refetch by hand, and 24 of them get cancellation wrong. Is a query library the right answer, and if so, when is it safe to adopt one?
2. **Types.** Ninety-two endpoint functions return untyped objects into screens with no PropTypes and no compiler. Is TypeScript's cost justified by what it would actually catch here?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Is a query library the right shape for server state | **Yes — TanStack Query is the target.** The measured duplication (80 async-state declarations, ~30 refetch callbacks, 24 unguarded effects) is precisely what it removes, and its cache, de-duplication and retry policy are the parts nobody re-implements correctly by hand | Keep hand-rolled effects forever: the counts above are the argument against. A homegrown `useAsync`: it fixes cancellation only, and a home-made cache is the part that goes wrong |
| Install it in this phase | **No.** This is a hardening phase, and the repository's own contract says a new dependency is proposed, not added quietly (AGENTS.md). Adopting it *with tests* needs a rendering library and a jsdom environment too — four packages and a Vitest configuration block — because no component test can run today | Installing it now: an untested migration of payment and catalog screens during the phase whose purpose is to make the system fail safely. Installing it without tests: worse |
| When it is adopted | **At the first screen that is rebuilt or newly written** — the storefront rebuild, the admin rebuild, or any new data screen, whichever comes first. The library, the jsdom environment and the rendering library arrive together in that change, with the first migrated screen's test as the proof | A big-bang migration of 29 screens: a large diff with no behaviour change, and exactly the "wholesale frontend rewrite" this programme forbids |
| What holds the line until then | **The in-repo guard idiom.** `useCatalog` already does this correctly with a local `active` flag; any screen touched adopts it. TD-25's own prescribed fix was one helper, and the pattern already exists to copy | A new abstraction introduced now and replaced at adoption: two migrations instead of one |
| TypeScript | **Deferred, with a trigger that can actually fire: a CI pipeline exists.** A type-check nobody runs is not a control. There is no CI in the repository (R-20/TD-31), so today TypeScript would be a 119-module toolchain change enforced by whoever remembers to run it | Adopting it now: cost paid immediately, enforcement deferred indefinitely. Deferring it with no trigger: how D-19 stalled for two phases |
| Licence | **Verified, not assumed.** TanStack Query 5.102.8 is MIT, with one transitive package (`@tanstack/query-core`) and a peer dependency on React 18 or 19 — compatible with this MIT repository | Assuming it: this repository has been burned twice by licence changes. FluentAssertions 8 forced a move to AwesomeAssertions (P-02), and MediatR is pinned at 12.x because 13 is commercially licensed |
| Bundle cost | **Accepted when adopted:** roughly 13 KB gzipped on a 121.6 KB first bundle, against ~30 refetch callbacks and 80 state declarations removed as screens migrate | Treating bundle size as decisive: no budget is enforced yet, and the figure is small relative to what it replaces |

## Decision

1. **TanStack Query is the target for server state.** D-19 is decided, not deferred: the question "what manages server state" now has a written answer, so no further screen is built against an open decision.
2. **Nothing is installed by this record.** The dependency, a jsdom environment and a rendering library arrive together in the first change that rebuilds or adds a data screen — with that screen's test as the evidence the pattern works.
3. **Until then, every screen that is touched adopts the existing guard idiom** (`useCatalog`'s local `active` flag). Guarding is not deferred; only the library is.
4. **TypeScript is deferred, and its trigger is re-worded** from "the start of Phase 16 or a CI type-check" — which fired without producing a decision — to **a CI pipeline exists** (R-20/TD-31). At that point the incremental path of [ADR-0035](0035-white-label-runtime.md) still applies: `allowJs`, new files typed, the pure feature modules converted first.
5. **The frontend remains never the authority.** A cache changes what is displayed, never what is true: prices, stock, permissions, coupon validity and module availability stay server decisions ([ADR-0028](0028-basket-and-pricing-pipeline.md)). No query cache may be treated as a source of truth for money.

## Consequences

- **Positive:**
  - D-19 stops being an open decision that every new screen has to work around; TD-23's "built twice" applies to a known destination instead of an unknown one.
  - The migration is paid for by work that was going to touch those screens anyway, rather than by a separate migration project.
  - The cancellation defect is addressed now, by an idiom already in the repository, without waiting for the library.
  - The TypeScript trigger can now actually fire, because it names a condition someone can create.
- **Negative / limits:**
  - Twenty-four screens keep their unguarded effects until each is touched. The failure is a superseded response painting stale data; it is not a correctness failure on the server, which re-prices and re-authorizes every request.
  - Two decisions that were bundled as one are now on different clocks, so D-19 is not "done" until TypeScript is settled too.
  - The adoption point is defined by an event (a rebuild) rather than a date. If the rebuilds slip again, this record should be revisited rather than silently re-deferred — that is the failure mode it exists to prevent.

## Revisit when

- The first storefront or admin screen is rebuilt, or a new data screen is added — the adoption point named above.
- A CI pipeline exists: the TypeScript trigger fires, and this decision's second half is taken.
- The rebuilds slip another phase without the library arriving: re-open this record rather than defer by default.
- A measured problem appears that a cache would cause — stale prices or stale stock shown as authoritative — which would change the shape of adoption, not the decision.

## Verification

- **Not verified by an automated test, and cannot be today.** There is no rendering library and no jsdom environment (TD-32), so neither the guard idiom nor a query layer can be asserted by a test in this repository yet. Establishing that environment is part of the adoption change in Decision 2.
- The counts in **Context** were produced by scanning `frontend/src` during Phase 17 and are reproducible by the same scan.
- The licence, version and dependency shape of TanStack Query were read from the package registry on 2026-09-12, not recalled.
