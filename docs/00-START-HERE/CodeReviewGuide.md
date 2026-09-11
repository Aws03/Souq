# Code review guide

> **What this page is:** what a reviewer of Souq actually looks for, in the order that catches the most damage per minute. It is written for this codebase — a multi-tenant commerce platform where a mistake can leak another store's data or charge the wrong amount — not as a generic checklist.
> **Use it on your own diff first.** Self-review with this list before asking anyone else.

## How to review, in order

1. **Read the description, then the tests, then the code.** If the change has no test, ask why before reading further.
2. **Find the business rule.** Which rule changed, and is it expressed where it belongs?
3. **Attack it.** Another store's id, a missing permission, a concurrent request, a provider timeout, a rounded number.
4. **Then** look at style. Naming and shape matter, but never before correctness and isolation.

## 1. Tenant isolation (the first question, every time)

- Could this code path return or modify a row belonging to **another store**? What happens if the id in the request belongs to one? (Expected: **404**, never 403, never data.)
- Does anything take a tenant id from the client — a request property, a header, a query string? That is never allowed outside the audited platform area.
- New entity: does it implement `ITenantOwned`, with the tenant in its composite keys? (Tests enforce this, but notice it in review.)
- New query service or repository method: does it rely on the global filter, and could it be reached with `IgnoreQueryFilters` or raw SQL? (Only the reviewed platform read path may bypass the filter.)
- Background work: does it run inside an explicit tenant scope?

## 2. Authorization and ownership

- Does the endpoint declare its access explicitly (`[HasPermission]`, `[Authorize]`, or a deliberate `[AllowAnonymous]`)? A test enforces this; the review question is whether the chosen permission is the *right* one.
- Is ownership checked **in the use case**, not only in the UI or the route? A customer may only touch their own orders, addresses and reviews.
- Does the permission match the risk? Cancelling an order and refunding money should not ride on the same permission as editing a product description.
- Platform endpoints: on the platform host only, behind a `platform.*` permission, and audited (`IAuditable`).

## 3. Money

- Are amounts computed on the **server**, from the server's own catalog and rules? Nothing from the client's basket total.
- Is `Money` used rather than a bare `decimal`, so currency travels with the amount and rounding follows the currency's minor units?
- Does the change alter a stored total? Order totals are frozen at placement; recomputing them later rewrites history.
- Payment paths: is the call outside the transaction? Is it idempotent (an idempotency key, or a check before acting)? What happens if the provider answers twice, or never?
- Refunds: can the total refunded exceed what was captured?

## 4. Stock and concurrency

- Does the change touch stock without going through Inventory's reservation contract?
- Under two simultaneous requests, can `available` go negative, or a coupon exceed its limit? The protections are `rowversion` plus bounded retry — is the new path inside them?
- Does every stock change still write exactly one ledger entry?
- Is a `DbUpdateConcurrencyException` handled deliberately (retry or 409), not swallowed?

## 5. Transactions and side effects

- Is the unit of work owned by the use case, with no network call inside it ([ADR-0021](../11-ADR/0021-transaction-boundaries.md))?
- Do side effects that must survive a crash (email, notifications) go through the **outbox**, written in the same save?
- If the transaction rolls back, does anything outside the database remain changed?
- Are `ExecuteUpdate`/`ExecuteDelete` used? They skip interceptors (no audit timestamps, no write guard) — is that deliberate and commented?

## 6. Module boundaries

- Does a handler reach into another module's aggregate, repository or tables instead of asking through a contract? (The tests only cover the Application layer; this is where a reviewer earns their keep.)
- Did the change add a new arrow between modules? Then the graph in [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) and the architecture test must change too — deliberately.
- Is a new concept in the module that **owns** it, or in the one that happened to need it first?

## 7. The domain model

- Is the new rule in the aggregate, or leaking into a handler, a controller, or React?
- Does the entity still guard its own state (no public setters, no `SetStatus`-style methods that let any caller pick any value)?
- Is a new field business state, or display state that belongs in a DTO?
- Does a new value object earn its place by carrying rules, or is it a wrapper around a string?

## 8. API contract

- Is the error a **stable code** a client can branch on, and is it documented? Never a message-only failure.
- Right status: 400 shape, 401 unauthenticated, 403 authenticated-but-forbidden, 404 missing *or another owner's*, 409 conflict, 422 rule violation, 503 dependency unavailable.
- Is a list paged, with a sort allowlist and a deterministic tiebreaker?
- Is this a breaking change to a response shape or a code? If so, is it staged (add, migrate, remove)?
- Does the response leak anything it should not: internal ids, another store's data, personal data beyond what the caller may see?

## 9. Database changes

- Is there a migration, and is it additive? If it moves data, is the move written by hand and reviewed, with the copy before the drop ([Migrations.md](../06-DATABASE/Migrations.md))?
- Does a new constraint the business relies on exist in the database as well as in code (uniqueness, precision, required relationships)?
- Do new hot queries have an index, and does the index lead with `TenantId` where the query filters by store?
- Does the new table have an owner recorded in [OwnershipMap.md](../06-DATABASE/OwnershipMap.md), and a lifecycle (does anything ever delete these rows)?

## 10. Security and privacy

- Are secrets read from configuration and never logged? Does any new log line carry a token, a password, card data, or personal data?
- New file upload: type detected from content, size capped, stored under the store's prefix?
- New token (reset, verification, invitation): hashed at rest, single use, expiring, generated at dispatch?
- Does an error message reveal whether a resource exists in another store, or whether an email is registered?

## 11. Frontend

- Does the UI decide anything the server must decide (price, permission, stock, tenancy)? Guards are UX only.
- Are module-gated features hidden *and* enforced server-side?
- Loading, error and empty states handled? Is the error message the server's translated code, not a raw dump?
- Are all strings translated, with no brand or currency literals (a test enforces the literals)?

## 12. Tests

- Is there a test at the level of the rule — domain rule → domain test — rather than only an end-to-end test?
- Does a bug fix include a test that fails without the fix?
- Are failure paths tested, not just the happy path?
- Was an existing assertion changed? Why? A weakened test is a red flag: it must be justified in the commit message.
- For anything touching money, stock, permissions or tenancy: is there an integration test?

## 13. Observability and operations

- Will a failure here be diagnosable from the logs (a correlation id, the use case, the store), without dumping sensitive data?
- Does the change add a startup dependency? Then it must fail fast with a clear message, not fail later in a request.
- Does it change configuration? Is the key documented in [Configuration.md](../09-OPERATIONS/Configuration.md), with its default and environment?

## 14. Documentation

- Behaviour changed → module document and business rules updated in the same commit.
- Endpoint, use case or schema changed → inventories regenerated.
- An architectural decision → an ADR, linked from the index.
- Something deliberately left undone → recorded as DEFERRED with a reason, not silently dropped.

## Comment policy

Comments in this codebase are in Arabic and exist to explain what code cannot:

**Keep or add a comment when it explains:** *why* a decision was made; an invariant that is not obvious; a security consideration; an external provider's limitation; concurrency behaviour; an architectural constraint ("this must stay outside the transaction").

**Remove a comment that:** restates the code; repeats the method name; describes a phase that has already shipped ("Phase 14 will add…" when Phase 14 is done); names a type that no longer exists; or contradicts the code next to it — a wrong comment is worse than none.

**In review:** if a reviewer asks "why does this do that?", the answer belongs in a comment or a test name, not only in the pull request.

## Review tone

Say what is wrong, where, and what you would do instead. Distinguish blocking issues (correctness, isolation, money, security) from preferences, and say which is which. If a change is good, say so — a review that only lists faults teaches nothing about what to repeat.
