# ADR-0030: Coupons: uses reserved at checkout as redemption records, start dates and per-customer limits

- **Status:** Accepted (implemented in Phase 10), 2026-09-11.
- **Fixes:** Phase 0 C1 for coupons. Two checkouts could both take a coupon's last use, because usage was counted only when payment was confirmed. Nothing recorded which order used which coupon.
- **Builds on:** [ADR-0013](0013-optimistic-concurrency.md) (`rowversion`), [ADR-0014](0014-money-precision.md) (rounding once), [ADR-0021](0021-transaction-boundaries.md) (no network call inside a transaction), [ADR-0026](0026-inventory-reservations.md) (retry from a fresh read), [ADR-0028](0028-basket-and-pricing-pipeline.md) (pricing pipeline) and [ADR-0029](0029-orders-lifecycle.md) (order transitions). It changes none of them.

## Context

Before Phase 10:
- **Counting:** `Coupon.UsedCount` went up when payment was confirmed. Every unpaid order had passed the `MaxUses` check against the same count, so concurrent checkouts on the last use all succeeded, and all of them could be paid.
- **Records:** an order kept the coupon code as text. Nothing linked a coupon to the orders and customers that used it, so a per-customer limit was impossible and admins couldn't see who had used a coupon.
- **Rules:** an expiry date, a global limit and a minimum order amount. There was no start date and no per-customer limit.
- **Deleting:** always a hard delete, even after the coupon had been used.
- **Cancelling:** a cancelled paid order kept its use.

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| When a use is taken | **At checkout, inside the order transaction.** `Coupon.Redeem` runs on a fresh read, after the order row is written and before the stock reservation. The coupon's `rowversion` serializes concurrent redemptions. The loser re-reads (up to 5 attempts, the Phase 6 inventory pattern) and either takes a use that is still free or is rejected with `422 InvalidCoupon`; then its whole checkout rolls back | At payment (the old way): the overshoot race. An atomic `UPDATE … WHERE UsedCount < MaxUses`: moves the rule out of the aggregate, and can't check the per-customer limit in the same statement |
| What records a use | **One `CouponRedemption` per order:** coupon, order, customer, the discount given, and a status (Reserved → Confirmed or Released). Unique per order | A counter only: no per-customer limit and no admin history. A list stored on the coupon: grows without bound and conflicts on every checkout |
| A cancelled order | **Its redemption is Released and the use goes back to the coupon,** in the cancellation's own transaction. This covers every cancel path: customer, admin, failed payment, expired checkout, failed payment start. Release is idempotent | Keeping the use: a customer who cancels loses their once-per-customer coupon, and an abandoned checkout burns a limited coupon |
| Payment | Marks the redemption Confirmed in the payment transaction. It takes no second use | Counting again at payment: double counting |
| Per-customer limit | **Counts the customer's active redemptions** (Reserved and Confirmed). It is checked in the quote when the customer is known, and again at reservation on a fresh read | Counting paid orders only: an unpaid order would let the customer start a second checkout with the same coupon |
| Start date | An optional `StartsAt`, which must be before `ExpiresAt` | — |
| Deleting a used coupon | **`409 CouponInUse`** once it has any redemption or use; deactivate it instead. Unused coupons are still hard-deleted | Soft delete for everything: clutter from coupons created by mistake. Hard delete always: orders lose the link to their coupon |
| Category or product scope | **Not built.** The roadmap lists it "if needed". No store needs it yet, and it needs a rule for mixed baskets (which lines the discount applies to) | Building it speculatively |
| Rounding | Unchanged: `Money.FromCalculation` rounds the discount once, to the currency's minor units | — |

## Decision

The options marked "Chosen" above. `UsedCount` now means "uses held by open or paid orders".

The migration is additive:
- Each existing order that used a coupon still present gets a redemption:
  - Pending → Reserved;
  - Paid, Shipped or Delivered → Confirmed;
  - Cancelled → none.
- Existing counters keep their value. It may include a paid order that was later cancelled; a count that is too high is the safe error.
- The pending orders are added to the counters. Those orders now hold a use that their payment will no longer add.

`Down()` subtracts the reserved uses before dropping the table. `MigrationRehearsalTests` checks the backfill on Phase 1 data.

## Consequences

- **Positive:**
  - A coupon can't exceed its limit under concurrency (C1 closed for coupons).
  - Per-customer limits and start dates work, in the quote and at checkout.
  - Admins see which orders and customers used a coupon, and the discount each got.
  - Cancelled and abandoned checkouts give their use back.
  - A used coupon can't be deleted by accident.
- **Negative / limits:**
  - Checkouts that use the same coupon wait briefly on its row. That is milliseconds: the transaction holds no network call, and the store's order-number row already serializes checkouts (ADR-0029).
  - An unpaid order holds its use until it is paid, cancelled or expired by the checkout sweep (Phase 6). A limited coupon can therefore read as used up while checkouts are open.
  - Refunds (Phase 11) will decide whether a refunded order gives its use back. Today only a cancellation does.
  - Automatic promotions, category or product scope, and stacking are not built.

## Revisit when

- A store asks for category- or product-scoped coupons, or for automatic promotions.
- One coupon's contention shows in checkout latency (for example, a flash-sale code). An atomic conditional update is the fallback.

## Verification

- **Domain:**
  - `CouponRuleMatrixTests`: every combination of active, window, global limit, per-customer limit and minimum order (64 cases) through the real entity; rounding; the window and limit guards; the redemption lifecycle.
  - `CouponTests`.
- **Application:**
  - `CouponRedemptionsTests`:
    - a reservation takes a use and records it as Reserved;
    - an exhausted per-customer limit is rejected without saving;
    - a concurrency conflict is retried from a fresh read that sees the winner;
    - release gives the use back once, and confirmation doesn't take one;
    - an order without a coupon confirms and releases nothing.
  - `PricingServiceTests`: the per-customer limit in the quote.
  - `CreateOrderHandlerTests`, `ConfirmOrderPaymentHandlerTests`: reserve at checkout, confirm at payment, release on a failed payment.
  - `CouponHandlersTests`: window and per-customer validation, `CouponInUse`.
- **Integration:**
  - `CouponRedemptionTests`:
    - five concurrent checkouts on a single-use coupon: one order, four `422 InvalidCoupon`;
    - the per-customer limit, released by customer and by admin cancellation and confirmed by payment;
    - a coupon that hasn't started yet;
    - the admin redemptions list;
    - `409 CouponInUse`.
  - `TenantIsolationTests`: the redemptions list across stores.
  - `MigrationRehearsalTests`: the backfill.
