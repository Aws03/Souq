# Reviews: change guide

> Read [README.md](README.md) first. This page lists common changes and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change**

1. Only a verified buyer may write a review, and the evidence (`OrderId`) is stored on the review.
2. One review per customer per product — enforced in `CreateReviewHandler` and by the unique index `(CustomerId, ProductId)`.
3. Public output and aggregates come from **approved** reviews only.
4. The moderation note is staff-only: never in a public DTO, never in audit metadata.
5. Every moderation decision records who and when, and writes an audit row.
6. The publishing policy is per store, read from the `Tenants` row at write time.
7. Every route stays behind `[RequiresModule(StoreModules.Reviews)]`; changing the policy needs `store.settings.manage` on top of `reviews.moderate`.
8. Reviews never writes to Catalog. No rating counter on `Product`.

**Files to read first**

`src/Souq.Domain/Entities/Review.cs`, `src/Souq.Application/Features/Reviews/Commands/CreateReviewHandler.cs`, `src/Souq.Application/Features/Reviews/Moderation/ReviewModeration.cs`, `src/Souq.Application/Features/Reviews/Queries/IReviewQueries.cs`, `src/Souq.Infrastructure/Persistence/Queries/ReviewQueries.cs`, `src/Souq.Infrastructure/Persistence/Configurations/ReviewConfiguration.cs`, `src/Souq.Application/Features/Stores/StoreReviewSettings.cs`, `src/Souq.API/Controllers/ReviewsController.cs`, and [ADR-0033](../../11-ADR/0033-review-moderation-and-wishlist.md).

**Tests that guard the module**

`ReviewTests`, `CreateReviewHandlerTests`, `ReviewModerationHandlersTests`, `ReviewSettingsHandlersTests`, `ReviewModerationTests`, `QueryServiceTests`, `TenantIsolationTests`, `AuthorizationMatrixTests`, and the frontend tests `frontend/src/features/reviews/ratingSummary.test.js` and `frontend/src/features/admin/reviews/reviewModeration.test.js`.

---

## I need to… change who may write a review (eligibility)

*Examples: allow a shipped order, require delivery within the last 90 days, allow one review per order line instead of per product.*

- **Inspect:** `CreateReviewHandler` (the ordered sequence of checks), `IOrderRepository.FindDeliveredOrderIdContainingAsync` and its implementation in `src/Souq.Infrastructure/Persistence/Repositories/OrderRepository.cs`, `IReviewRepository.HasCustomerReviewedProductAsync`, `ReviewConfiguration`'s unique index.
- **Rules to respect:** eligibility spans two aggregates, so it stays in the handler — do not teach `Review` about orders. Keep the check order (cheap and definitive first: blocked, then already-reviewed, then the order query). Keep storing the order that justified the review. If you relax "one per product", the unique index is the real constraint and must change with it.
- **Steps:** decide the rule → change or add a repository method so it stays **one** SQL query (the current one is a single indexed lookup; do not load the customer's orders into memory) → adjust the handler and the `NotEligible` message → if the rule becomes time-based, take the clock from `TimeProvider`, never `DateTime.UtcNow`.
- **Tests:** `CreateReviewHandlerTests` for each branch; the `QueryServiceTests` case that a paid-but-undelivered order cannot review will need updating if you widen it; add an integration case in `ReviewModerationTests` if the change is visible end to end.
- **API:** the 422 `NotEligible` code stays — the frontend message is keyed on it. Widening eligibility is backward compatible; narrowing it is not.
- **Database:** a changed uniqueness rule needs a migration and a decision about existing duplicates (there can be none today, so a plain index change is enough).
- **Security:** never accept an order id or customer id from the client; the server must keep deriving both.
- **Docs and ADR:** README's eligibility section; an ADR amendment if "verified purchase" itself changes meaning.

## I need to… add store replies or review editing (DEFERRED)

- **Inspect:** `Review`, `ReviewConfiguration`, `ReviewDto` and `AdminReviewDto`, `ReviewQueries`, `frontend/src/components/reviews/ReviewList.jsx`.
- **Rules to respect:** ADR-0033 deferred both because **each needs an edit history**: a published review that silently changes, or a reply that can be rewritten after the fact, destroys the record's credibility. Decide the history model first (an owned child collection of revisions, or an append-only table), then build. A reply is store-authored content and needs its own moderation story — it is published immediately by definition, so it must be authored only by `reviews.moderate` holders and audited. Editing must re-enter moderation in a store that moderates; otherwise a customer could get an innocuous review approved and then rewrite it.
- **Steps:** ADR first → model (reply text, author user id, time; or revision rows) → domain methods with guards → configuration and migration → commands, handlers and endpoints under the moderation controller (replies) or the customer route (edits) → read port and DTOs → frontend.
- **Tests:** domain tests for the new transitions and history; handler tests for permissions; an integration test that an edit in a moderating store returns to Pending.
- **API:** additive endpoints and fields.
- **Database:** additive tables or columns; reversible.
- **Security:** replies are public store speech — audit them; keep the moderation note separate and still private.
- **Docs and ADR:** a new ADR, referenced from ADR-0033 and from README's "Future evolution".

## I need to… add moderation notifications (DEFERRED, now unblocked)

*Staff learn a review is waiting; the customer learns the decision.*

- **Inspect:** [../Notifications/README.md](../Notifications/README.md), `NotificationKinds`, `INotificationOutbox`, `OrderStatusChangedHandler` as the fan-out example (`NotifyStaffAsync`), `NotificationMessageTypes`' allow-list, `frontend/src/features/notifications/notificationView.js`.
- **Rules to respect:** the use case must **not** send anything itself. It enqueues a reference (review id) in its own unit of work; a Notifications handler reads the live state and writes notification rows or an email. Payloads carry ids only — never the comment text, which is user input and personal-ish content. Staff fan-out goes through `RolePermissions.RolesGranting(Permissions.Reviews.Moderate)`. A rejection message to the customer must not leak the staff-only note.
- **Steps:** define the message type and add it to the allow-list → register the handler in `src/Souq.Application/DependencyInjection.cs` → enqueue from `CreateReviewHandler` (pending only) and from `ReviewDecision.ApplyAsync` → add notification kinds and, if you want email, an `EmailTemplate` entry with Arabic and English text → frontend text and link in `notificationView.js`.
- **Tests:** handler unit tests in `tests/Souq.Application.Tests/Notifications/NotificationHandlersTests.cs`; extend `CreateReviewHandlerTests` and `ReviewModerationHandlersTests` to assert the enqueue; an integration case in `ReviewModerationTests` that dispatches the outbox and reads the notification.
- **API:** none new; the bell and `/api/notifications` already carry it.
- **Database:** none (rows go into the existing `Notifications` and `OutboxMessages`).
- **Security:** no note, no comment text in the payload or the log.
- **Docs and ADR:** ADR-0033 named this as the revisit trigger; update it, README here and the Notifications README.

## I need to… change the publishing policy model

*Examples: auto-approve only for 4–5 stars, or auto-approve for repeat customers.*

- **Inspect:** `Tenant.ReviewsAutoApprove` and `Tenant.SetReviewsAutoApprove`, `src/Souq.Application/Features/Stores/StoreReviewSettings.cs`, `CreateReviewHandler` (where the policy is read), `ReviewModerationController`'s settings endpoints, `frontend/src/pages/admin/ReviewModeration.jsx`.
- **Rules to respect:** the policy is **Platform-owned store configuration**, not review state. ADR-0033 deliberately kept it as a column rather than a field in the settings JSON, because that document is replaced wholesale by its PUT and an older client would reset it — keep that property. Read it from the `Tenants` row at write time, never from the cached directory. Changing the policy must not retro-approve pending reviews (`Tenant.SetReviewsAutoApprove` says so explicitly).
- **Steps:** extend the store-side model (a column or a small owned value object) → `Tenant` method with validation → migration with a backfill that preserves today's behaviour → `ReviewSettingsDto` and both handlers → the decision in `CreateReviewHandler` → the admin control.
- **Tests:** `TenantTests` for the new default, `ReviewSettingsHandlersTests`, `CreateReviewHandlerTests` for each branch, the policy scenario in `ReviewModerationTests`.
- **API:** `ReviewSettingsDto` gains fields — additive; keep `autoApprove` meaningful for existing clients or version the endpoint.
- **Database:** additive column plus a backfill so existing stores keep behaving as before (follow the `Phase13ReviewsWishlist` pattern).
- **Security:** writing the policy keeps needing both permissions.
- **Docs and ADR:** ADR-0033 amendment; README's use-case table.

## I need to… change the public aggregates or show ratings on product cards

- **Inspect:** `IReviewQueries.ListForProductAsync`, `ReviewQueries` (the grouped query, the rounding, the first-name rule), `ProductReviewsDto`, `RatingCountDto`, `frontend/src/features/reviews/ratingSummary.js`, `frontend/src/components/reviews/RatingSummary.jsx`.
- **Rules to respect:** approved only. Keep the public list at a constant number of queries — `QueryServiceTests` asserts at most three whatever the page size, which is the fix for the historical N+1. Never push a counter into `Product`: Modules.md forbids Reviews writing to Catalog, and a stored counter would have to be corrected on every moderation decision.
- **Steps for cards:** decide where the aggregate lives — a cached read model or a materialized table owned by Reviews and refreshed on decisions — then expose it through `IReviewQueries` and let Catalog's listing join it in **Infrastructure** only.
- **Tests:** extend `QueryServiceTests` (query count and values) and `ReviewModerationTests` (aggregates follow each decision).
- **API:** additive fields on the product list; the frontend should treat a missing aggregate as "no ratings".
- **Database:** a read model needs a migration and a rebuild path; document how it is rebuilt from `Reviews`.
- **Security:** aggregates are public; keep names out of them.
- **Docs and ADR:** this is the ADR-0033 revisit trigger for the storefront — write the ADR before the read model.

## I need to… change what staff see or filter in the queue

- **Inspect:** `ReviewModerationFilter`, `ListReviewsForModerationQuery` and its validator, `ReviewQueries.ListForModerationAsync`, `AdminReviewDto`, `frontend/src/features/admin/reviews/reviewModeration.js` (`buildReviewQuery`, `REVIEW_STATUSES`, `STATUS_BADGE`).
- **Rules to respect:** paging goes through `PagedQueryValidator` (max page size 100). Keep the join count constant. Product names follow the store's default culture. The frontend's status list and the server's enum must not drift — `buildReviewQuery` drops unknown values rather than passing them on.
- **Steps:** extend the filter record and the validator → the query → the DTO → the page and its test.
- **Tests:** `reviewModeration.test.js`, plus a queue assertion in `ReviewModerationTests`; `TenantIsolationTests` must stay green (a filtered queue must still be empty across stores).
- **API:** additive query parameters.
- **Database:** a new filter may need an index — `(TenantId, Status, CreatedAt)` covers status and recency today.
- **Security:** the queue is `reviews.moderate` only; it shows full customer names, so do not reuse its DTO for public output.
- **Docs:** README's use-case table.

## I need to… make a review disappear from the storefront right now

- **Operational answer:** reject it (`POST /api/admin/reviews/{id}/reject`). Rejection hides it from the list and the aggregates immediately, keeps the evidence, prevents a rewrite, and leaves an audit trail. A later approve restores it and clears the note.
- **Do not** delete the row: the customer could then post again, and the `Restrict` foreign keys make deletion a 409 anyway once anything references it.
- **If the whole feature must go dark for a store:** turn off the `reviews` module from the platform area — every route, public and admin, then answers 404 `ModuleDisabled`, and turning it back on restores the data untouched (`ReviewModerationTests` covers both directions).

## I need to… add abuse protection (FUTURE)

- **Inspect:** `CreateReviewHandler`, the rate-limiting setup under `RateLimiting` in `src/Souq.API/appsettings.json` and `src/Souq.API/Program.cs`, `Review`'s comment guard.
- **Rules to respect:** an automatic filter decides *publishing*, never *deletion* — route a suspicious review to Pending instead of rejecting it, so a human decides. Keep the classifier behind an interface in Application and its implementation in Infrastructure (rule 3 of the repository's layering); never call an external service from the request path — enqueue an outbox message and let a handler classify, exactly as email does.
- **Tests:** handler tests for the routing decision; do not assert a third-party service's behaviour.
- **Security and privacy:** sending review text to an external classifier is a data-processing decision — ADR, and no text in logs.
- **Docs and ADR:** ADR required; ADR-0033 lists abuse patterns as a revisit trigger.
