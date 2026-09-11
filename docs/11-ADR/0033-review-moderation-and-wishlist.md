# ADR-0033: Reviews and wishlist: moderation under a per-store publishing policy, approved-only aggregates, and a server-side wishlist that absorbs the guest list

- **Status:** Accepted (implemented in Phase 13), 2026-09-11.
- **Builds on:**
  - [ADR-0008](0008-cqrs-strategy.md): read ports with projections.
  - [ADR-0022](0022-tenancy-enforcement.md): the tenant filter and write guard, which scope every review and wishlist row to its store.
  - [ADR-0024](0024-platform-administration.md): module flags (D-11) and the audit behavior.
  - [ADR-0027](0027-customer-profile-and-erasure.md): customer erasure.
  - [ADR-0028](0028-basket-and-pricing-pipeline.md): the guest-to-customer merge at sign-in.

## Context

Before Phase 13:
- **Reviews:** every verified-purchase review was published at once. A store couldn't hide an abusive one, and the average counted all reviews.
- **Wishlist:** it lived only in `localStorage`. It stayed in one browser, wasn't tied to the account, and didn't follow the customer to another device.
- **Module flags:** the `reviews` flag closed the public review endpoints. The `wishlist` flag existed (D-11) but had no server surface to close.

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Review states | **Pending, Approved and Rejected** on `Review`, with the transitions guarded in the entity: <br>• Pending → Approved or Rejected; <br>• Approved ↔ Rejected; <br>• repeating a decision changes nothing. <br>The review stores the moderator, the time and a note for staff only. Every decision is audited | A boolean `IsApproved`: it can't tell "not checked yet" from "hidden on purpose". Deleting rejected reviews: destroys the evidence and lets the customer post again |
| Publishing policy | **Per store, `Tenant.ReviewsAutoApprove`.** It is read from the store row when the review is written. New stores start with moderation. Stores that existed before Phase 13 were migrated to `true`, because they already published at once | One platform-wide setting: stores differ. A field in the settings JSON: that PUT replaces the whole document, so an older client would silently reset it. The cached tenant directory: a policy change would lag |
| Who changes the policy | **Moderators (`reviews.moderate`) read it. Changing it also needs `store.settings.manage`** | Moderators alone: a staff member could switch moderation off for the whole store |
| Aggregates | **Count, average and a per-star distribution over approved reviews only.** The read port computes them in one grouped query, so the public list stays at three constant queries | Counters stored on `Product`: Reviews would write to Catalog (forbidden in Modules.md), and every moderation change would have to keep them consistent |
| Existing reviews | **Migrated to Approved**, since they were public, with no moderator: it was the policy before moderation, not a decision | Pending: every existing review would disappear overnight |
| After a rejection | **No second review** (one review per customer and product is still a unique index). A customer in a moderated store is told the review awaits the store | Editing or resubmitting: needs an edit history; out of scope |
| Wishlist storage | **`WishlistItem` rows** (customer, product, unique, at most 200 per customer) in the Shopping module. Name, price, stock and image are read live from the catalog. Products that are no longer published are hidden, not deleted | A snapshot of product data: a wishlist is not an invoice, and stale prices mislead. Staying in `localStorage`: nothing follows the customer |
| Guest list | **Stays in the browser.** It merges at sign-in (`POST /api/wishlist/merge`), and the local copy is then cleared. Unknown, foreign, unpublished and over-limit ids are skipped silently | Server-side guest wishlists behind a cookie, like the basket: more state and clean-up for a feature guests rarely use. Failing the merge on a bad id: a stale local list would break sign-in |
| Adding | **`PUT /api/wishlist/{productId}`, idempotent.** The unique index is the last guard against double clicks (409 from the unit of work) | `POST`, which duplicates |
| Module flags | **`[RequiresModule]` on every controller.** A disabled module returns `404 ModuleDisabled` for the public list, review creation, moderation, the policy and every wishlist route. The frontend falls back to the local list when the wishlist is off | Hiding the features only in the UI |
| Erasure | **The wishlist is personal data and is deleted with the customer.** Reviews stay, pointing at the anonymised profile, as before | Keeping it |

## Decision

The options marked "Chosen" above. Migration `Phase13ReviewsWishlist` is additive:
- on `Reviews`: `Status` (default 0) and the moderation columns;
- on `Tenants`: `ReviewsAutoApprove` (default 0);
- the `WishlistItems` table and its indexes.

A hand-written backfill in the same migration sets every existing review to Approved and every existing store to publish at once. Nothing is dropped.

## Consequences

- **Positive:**
  - Stores can moderate, and the aggregates reflect only what is published.
  - Moderation decisions leave an audit trail.
  - The wishlist follows the customer across devices, with live prices.
  - Both module flags are enforced on the server.
- **Negative / limits:**
  - Customers aren't told about moderation decisions, and staff aren't alerted to new pending reviews. Notifications arrive in Phase 14.
  - Reviews can't be edited or answered, and a rejected customer can't post again.
  - Ratings appear only on the product page, not on product cards. Listing aggregates would need a cached or materialised read model (Phase 16, storefront).
  - The guest list belongs to one browser. There are no back-in-stock or price-drop alerts.
  - Eligibility still asks `IOrderRepository` whether the product was delivered. `IOrderHistory` waits for a second consumer.

## Revisit when

- Phase 14 adds notifications: pending reviews for staff, decisions for customers.
- The storefront (Phase 16) needs ratings on cards.
- Abuse patterns call for automatic filtering (spam, profanity).

## Verification

- **Domain:**
  - `ReviewTests`: states, transitions, idempotence and guards.
  - `TenantTests`: new stores start with moderation.
  - `WishlistItemTests`: owner guards and the limit.
- **Application:**
  - `CreateReviewHandlerTests`: pending or approved according to the store policy.
  - `ReviewModerationHandlersTests`: approve and reject, 404, authentication, the note validator, the audit record without the note.
  - `ReviewSettingsHandlersTests`.
  - `WishlistHandlersTests`: an idempotent add; 404 for unpublished or foreign products; the limit; removal; the merge's order, skips and cap; guests and staff refused.
- **Architecture:** the Shopping module owns `Wishlist`.
- **Integration:**
  - `ReviewModerationTests`:
    - pending → approved or rejected → approved again, with the aggregates following each step;
    - the policy switch;
    - staff can moderate but not change the policy;
    - both modules disabled ⇒ `404 ModuleDisabled` on every route, reopened when re-enabled.
  - `WishlistTests`:
    - an idempotent add, newest first with live price and stock;
    - an archived product hidden, then shown again when republished;
    - 404s; 401 for a guest and 403 for an admin account;
    - the merge skipping duplicates, unknown and foreign products;
    - erasure deleting the wishlist.
  - `TenantIsolationTests`:
    - the moderation and wishlist routes return 404 from another store;
    - the moderation lists are empty there;
    - the merge ignores another store's products.
  - `QueryServiceTests`: the default store still publishes at once after the migration.
- **Frontend (Vitest):** `wishlistModel`, `ratingSummary`, `reviewModeration`.
