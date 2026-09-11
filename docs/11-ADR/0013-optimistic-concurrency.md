# ADR-0013: Optimistic concurrency with `rowversion`

- **Status:** Accepted and implemented in Phase 1A, 2026-09-11, partially superseded by [ADR-0026](0026-inventory-reservations.md) (checkout conflicts and admin stock edits)
- **Date:** 2026-09-11
- **Related modules:** Catalog, Promotions, Ordering, Payments; Cross-cutting (conflict translation in the unit of work)
- **Related ADRs:** builds on [ADR-0007](0007-database-strategy.md); conflicts map to status codes through [ADR-0017](0017-error-contract.md); the re-read rule is standardized by [ADR-0021](0021-transaction-boundaries.md); partially superseded by [ADR-0026](0026-inventory-reservations.md) (bounded retry, and stock corrections instead of compare-and-set); the same token is reused by [ADR-0029](0029-orders-lifecycle.md), [ADR-0030](0030-coupon-redemptions.md) and [ADR-0031](0031-payments-and-refunds.md)

## Context

Phase 0 findings:
- **C1:** no concurrency token exists. Two checkouts for the last unit both pass `CanFulfill` on their own in-memory copies and both save, which oversells. The same race hits `Coupon.UsedCount` and simultaneous payment confirmations (client + webhook).
- **C4:** the product edit form writes back an absolute stock value it read earlier, overwriting sales made in between.

## Problem

How do we make concurrent writes to the same aggregate safe without a large throughput or complexity cost?

## Options considered

| Option | Correct? | Cost |
|---|---|---|
| **`rowversion` optimistic concurrency** (EF concurrency token) + conflict → 409 | ✅ | Tiny. Conflicts only when the same row is written simultaneously. |
| Atomic conditional `UPDATE … SET Stock = Stock - @q WHERE Stock >= @q` | ✅ for stock | Rule leaves the aggregate; bypasses the unit of work; one pattern per hot field |
| Pessimistic locks (`UPDLOCK`/`SERIALIZABLE`) | ✅ | Deadlock risk; locks held around slow calls; throughput loss |
| Do nothing | ❌ | Oversell and lost updates |

## Decision

1. `RowVersion` (a SQL Server `rowversion`, an EF **shadow** property, so the Domain stays persistence-free) on **Products, Coupons, and Orders**.
2. `AppDbContext.SaveChangesAsync` translates:
   - `DbUpdateConcurrencyException` → `ConcurrencyConflictException`;
   - unique-index violations (SQL 2601/2627) → `UniqueConstraintViolationException`.

   Both are Application types, so no EF type crosses the boundary. The API returns **409**.
3. **Checkout:** a conflict aborts the whole unit of work (nothing saved). The customer gets 409 "stock changed, please try again", and the retry sees the true stock. There is no automatic retry yet.
4. **Payment confirmation:** the loser of a client/webhook race re-reads the order status without tracking. If it is already Paid, it returns the idempotent success. It never double-counts a coupon or double-sends an email.
5. **Admin stock edits (C4):**
   - The form sends `stockQuantity` **only when the admin changed it**, together with `expectedStockQuantity`, the value the admin saw.
   - The handler rejects the update with 409 if the current stock differs (compare-and-set).
   - Editing only the name or price no longer touches stock at all.

## Why

- It is the smallest change that makes every race **correct**. The rules stay in the aggregates.
- Conflicts are rare at the expected traffic, and a clear 409 is an honest answer.

## Consequences

- Clients must handle 409 (the admin UI shows the server message; checkout shows the error and lets the customer retry).
- Under flash-sale contention on one product, customers could see repeated 409s.

## Revisit when

- Phase 6 (inventory reservations): consider an automatic bounded retry, or an atomic conditional update for the reservation step, if contention is measured.
- If per-field conflicts become common, consider ETag/If-Match on admin edits.
