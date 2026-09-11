# ADR-0029: Orders: per-store numbers, public tracking tokens, placement, and one transition table

- **Status:** Accepted (implemented in Phase 9), 2026-09-11.
- **Fixes:** Phase 0 B8. Anonymous tracking by sequential id exposed every order's status, tracking number and admin notes.
- **Builds on:** [ADR-0013](0013-optimistic-concurrency.md) (`rowversion`), [ADR-0021](0021-transaction-boundaries.md) (no network call inside a transaction), [ADR-0026](0026-inventory-reservations.md) (reservations) and [ADR-0028](0028-basket-and-pricing-pipeline.md) (basket, pricing pipeline). It changes none of them.
- **Date:** 2026-09-11
- **Related modules:** Ordering; Shopping (checkout from the basket); Payments (gateway-first cancellation); Inventory
- **Related ADRs:** builds on [ADR-0013](0013-optimistic-concurrency.md), [ADR-0021](0021-transaction-boundaries.md), [ADR-0026](0026-inventory-reservations.md) and [ADR-0028](0028-basket-and-pricing-pipeline.md); coupon uses are released on its cancellation paths in [ADR-0030](0030-coupon-redemptions.md); refunds and the staff cancellation path arrive with [ADR-0031](0031-payments-and-refunds.md); the shipping snapshot and total with [ADR-0032](0032-shipping-methods.md); its transitions raise the domain events of [ADR-0034](0034-notifications-outbox.md)

## Context

Before Phase 9:
- **Order ids:** customers saw the database id. It is sequential across every store, so it leaked sales volume and allowed enumeration.
- **Tracking:** `GET /api/orders/{id}/tracking` was anonymous, and it returned the status history including admin notes (B8).
- **Totals:** they were recomputed from the lines on every read. Nothing marked the moment an invoice became fixed.
- **Transition rules:** each method of `Order` held its own rules, and the admin UI kept a second copy in JavaScript.
- **History:** it recorded what happened, but not who did it.
- **Customers** could not cancel an order.
- **Checkout** still received its lines from the client, although the basket had lived on the server since Phase 8.

## Problem

How should an order be identified to its customer without exposing a store's sales volume or allowing enumeration, and how can an anonymous tracking link show progress without showing admin notes? And where do the transition rules, the invoice amounts, the actor behind each change and the checkout's lines live, so that each has one source of truth?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Order number | **An integer per store, starting at 1001.** A counter row per store is incremented atomically (EF `ExecuteUpdate`) inside the checkout transaction. The row lock serializes one store's checkouts until commit, and a rolled-back checkout gives its number back | The database id: shared by all stores, enumerable, leaks volume. `MAX + 1` with a unique index: needs retry logic inside the checkout transaction. A SQL `SEQUENCE` per store: DDL at runtime, and raw SQL outside migrations |
| Public tracking | **A random 128-bit token per order** (`GET /api/orders/track/{token}`). It returns the status timeline and the shipment only: no notes, no actor, no address, no amounts. The id-based route is removed | A hash of the id: reversible by enumeration. Signed URLs: expiry and key rotation for a link customers bookmark |
| Storing the token | Plain text (32 hex characters), unique per store, so the owner can copy the link again from the order page | Storing only a hash: the owner could never display the link again |
| Freezing totals | **`Order.Place(now)`** requires lines and a number. After it, lines and discount can't change, and the subtotal and total are stored (`PlacedSubtotal`, `PlacedTotal`); lists read them without summing | Recomputing on read: nothing marks the invoice moment, and every list sums lines |
| Transition rules | **One table, `OrderTransitions`:** Pending → Paid or Cancelled; Paid → Shipped or Cancelled; Shipped → Delivered; nothing leaves Delivered or Cancelled. Every transition method goes through it. A customer may only cancel a pending order. The admin UI receives `allowedActions` computed from the same table | Rules repeated in each method and in the UI: they drift |
| Who made a change | Each history row stores the actor kind (System, Customer, Staff, PaymentGateway) and the user id. The admin detail view resolves staff names | The audit log only: not every transition is an audited request, and the order page should show it |
| Customer cancellation | **The owner only, while Pending.** If a payment intent exists, the gateway is asked to cancel it first, outside any transaction: <br>• it had already succeeded ⇒ the payment is confirmed and the cancellation refused (`OrderAlreadyPaid`); <br>• it is still processing ⇒ refused for now (`PaymentProcessing`); <br>• it was cancelled ⇒ the order is cancelled and its reservation released in one transaction | Letting customers cancel paid orders: that needs refunds (Phase 11). Cancelling without asking the gateway: an order paid a second earlier could be cancelled |
| Checkout from the basket | `POST /api/orders` without items reads the customer's basket through Shopping's `IBasketCheckout`. The purchased quantities leave the basket when payment is confirmed, in the same transaction, so a failed payment leaves the basket intact | Clearing the basket when the order is created: a failed payment would lose it. The client keeps sending lines: two sources of truth |
| Billing address | A single-line snapshot: the chosen book address, else the default billing address, else the shipping address | A structured billing address now: Phase 12 designs structured addresses together with shipping rates |
| Detail per viewer | One query. The handler strips notes and actors for customers, and adds `allowedActions` only for `orders.manage` | Two queries: duplicated projection |

## Decision

The options marked "Chosen" above. The migration backfills existing orders before creating the unique indexes:
- numbers from 1001 per store, in creation order;
- a random token from `NEWID`;
- billing address set to the shipping address;
- placement at creation time, with totals computed from each order's lines and discount;
- each store's counter set to its highest number.

`MigrationRehearsalTests` checks all of it on Phase 1 data.

## Consequences

- **Positive:**
  - Orders can no longer be enumerated (B8 closed).
  - Numbers are readable and don't reveal how busy another store is.
  - Invoice amounts can't change after placement.
  - Transitions have one source of truth.
  - Every order records who did what.
  - Customers can back out of an unpaid order.
  - The basket drives checkout.
- **Negative / limits:**
  - One store's checkouts wait on the counter row for the length of the checkout transaction. That is milliseconds, since the transaction holds no network call.
  - Numbers are unique but not contiguous. A payment failure after commit leaves a cancelled order that keeps its number.
  - Backfilled tokens come from `NEWID` (122 random bits), which is enough for a link that reveals only status and shipment.
  - A snapshot of the shipping method and structured addresses: Phase 12. Refunds for cancelled paid orders: Phase 11.
  - A customer's cancellation reason is free text that staff can see.

## Verification

- **Domain:** `OrderLifecycleTests`: the 5×5 transition matrix, the customer rule, who acted, placement immutability, numbers, tokens, billing.
- **Application:**
  - `CancelMyOrderHandlerTests`: owner only, Pending only, and each gateway outcome.
  - `GetOrderByIdHandlerTests`: the detail shaped for each viewer.
  - `CreateOrderHandlerTests`: number and placement, basket checkout.
  - `BasketCheckoutTests`.
- **Integration:**
  - `OrderLifecycleTests`: numbers per store from 1001; the basket consumed only after payment; customer cancellation releasing the reservation; token tracking without notes, with the old route gone; admin search, filters and actors; frozen totals.
  - `TenantIsolationTests`: the token route and customer cancellation.
  - `AuthorizationBoundaryTests`: the reviewed public surface.
  - `MigrationRehearsalTests`: the backfill.
