# ADR-0026: Inventory: per-variant stock, explicit reservations, and checkout expiry

- **Status:** Accepted (implemented in Phase 6), 2026-09-11.
- **Decisions implemented:**
  - D-15 (background jobs: a .NET hosted service);
  - the Phase 6 part of D-21 (stock lives on the variant).
- **Fixes:**
  - Phase 0 C1 (oversell under concurrency), completed;
  - C4 (absolute stock edits);
  - the C6 residual (abandoned Pending orders).
- **Builds on:** [ADR-0013](0013-optimistic-concurrency.md), [ADR-0021](0021-transaction-boundaries.md) and [ADR-0025](0025-catalog-model.md). It changes none of them.
- **Date:** 2026-09-11
- **Related modules:** Inventory; Ordering; Catalog; Payments (intent cancellation)
- **Related ADRs:** builds on [ADR-0013](0013-optimistic-concurrency.md), [ADR-0021](0021-transaction-boundaries.md) and [ADR-0025](0025-catalog-model.md), and replaces the checkout-conflict and stock-edit parts of [ADR-0013](0013-optimistic-concurrency.md); baskets are excluded from reservations by [ADR-0028](0028-basket-and-pricing-pipeline.md); its reservations drive [ADR-0029](0029-orders-lifecycle.md) and the release path of [ADR-0030](0030-coupon-redemptions.md); its retry pattern is reused for refunds by [ADR-0031](0031-payments-and-refunds.md); low stock becomes a domain event in [ADR-0034](0034-notifications-outbox.md)

## Context

Before Phase 6 stock was a column on `Product`:
- Placing an order decremented it immediately and wrote a `Sale` ledger line. A cancellation or a failed payment put it back.
- An abandoned Pending order held its stock forever (the C6 residual).
- The admin form set an absolute value. That was mitigated in 1A by compare-and-set, but it still captured no reason.
- The loser of two concurrent checkouts for the last unit got a generic 409.
- The ledger could not tell a hold from a sale.
- Ordering mutated Catalog entities directly (Architecture.md §6, "Today").

## Problem

How do we hold stock for unpaid orders, keep an honest ledger, sell the last unit exactly once under concurrency, and release abandoned holds? And how do we do it without holding a transaction across a network call ([ADR-0021](0021-transaction-boundaries.md)), and without moving business rules out of the Domain?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Where stock lives | `InventoryItem` per variant (Inventory module): `OnHand`, `Reserved`, `Available = OnHand − Reserved`, low-stock threshold, `rowversion`. Database check constraints guard `0 ≤ Reserved ≤ OnHand`. | On `Product`: catalog edits and checkouts would share one hot row, and per-variant stock would need another migration later. |
| Holding stock for unpaid orders | Explicit `StockReservation` rows (reference, quantity, status, expiry). Checkout reserves, payment commits (the only place a `Sale` line is written), cancellation or payment failure releases, and cancelling a paid order restocks. | Decrement at checkout (the old model): the ledger records sales that never happen, and every abandonment path needs its own compensation. Decrement only at payment: oversells between checkout and payment. |
| Concurrency | Optimistic `rowversion` on the item plus a bounded retry (5 attempts) that re-reads the committed values. Inside the caller's transaction, EF's automatic savepoints drop the failed attempt's rows. The loser of the last unit therefore gets `422 InsufficientStock`, not a transient 409. | Pessimistic row locks (`UPDLOCK`): needs raw SQL or lock hints, which the architecture tests forbid outside migrations. A conditional `UPDATE … WHERE OnHand − Reserved ≥ @q`: moves the rule out of the Domain. Returning 409 to the customer: that was the old behaviour, and a transient conflict is not a customer error. |
| Ledger | One `StockMovement` per on-hand change, created only by `InventoryItem` (internal constructor), so Σ movements = `OnHand`. The migration inserts an opening balance where the history did not match. | Ledger lines for holds: mixes holds with stock. The reservation rows are the record of holds. |
| Stock corrections | `AdjustStock` (a delta plus a reason, audited) and `SetLowStockThreshold` in the Inventory area. The product form no longer carries stock. | An absolute value with compare-and-set (the 1A mitigation): an admin retrying with the refreshed value still overwrites intent, and no reason is kept. |
| Module boundaries | Ordering calls the Inventory contracts (`IInventoryReservations`, `IStockAvailability`). Catalog defines the port `IVariantStockInitializer`, and Inventory implements it (dependency inversion keeps the arrow Inventory → Catalog). The architecture test allows exactly these `*.Contracts` references and rejects cycles. | Catalog calling Inventory directly: a cycle. A domain event: there is only one consumer, and Architecture.md §6 says to use a direct call until a second one exists. |
| Expiry | An Ordering use case, `ExpireStaleCheckouts`, run for each active store by a hosted service every 60 s. It asks the gateway to cancel the payment intent first: already succeeded → confirm the order normally; still processing → try again next sweep; cancelled → cancel the order and release its reservation. | Inventory expiring holds on its own: the order would stay Pending, and a late payment would commit a hold that was already released. Hangfire: not needed yet (D-15). |
| Reservation lifetime | 30 minutes by default (`Inventory:ReservationMinutes`, 5–1440, validated at startup). | — |

## Decision

- **Domain:**
  - `InventoryItem`: `Receive`, `Adjust`, `Reserve`, `Commit`, `Release`, `Restock`, `SetLowStockThreshold`.
  - `StockReservation`, `ReservationStatus`, and `StockMovement` (which gains `InventoryItemId`).
  - `InvalidInventoryOperationException` (422).
  - `Product` no longer has any stock members.
- **Application:**
  - `Features/Inventory/Contracts` holds the contracts.
  - `Features/Inventory/Reservations` holds `InventoryReservations`, `InventoryWriter` (save with retry), `VariantStockInitializer` and `InventorySettings`.
  - Commands: `AdjustStock` and `SetLowStockThreshold`.
  - Ordering:
    - `CreateOrderHandler` saves the order and reserves its lines in one transaction, then creates the payment intent outside it.
    - `OrderPaymentConfirmation` commits the reservation on success and cancels and releases it on failure.
    - `UpdateOrderStatus` hands a cancellation to Inventory.
    - `ExpireStaleCheckouts` settles expired reservations.
  - `IPaymentService.CancelIntentAsync` (Stripe: read, then cancel, re-reading on a race; fake gateway: cancelled).
- **Infrastructure:**
  - `InventoryRepository`, configurations with composite tenant-scoped foreign keys, and `ReservationExpiryService`.
  - `ITenantDirectory.ListActiveAsync`.
  - Catalog and inventory projections read availability from `InventoryItems`.
- **API:** `POST /api/admin/inventory/{productId}/adjustments` and `PUT /api/admin/inventory/{productId}/threshold` (`inventory.manage`, audited). The inventory list returns on-hand, reserved and available.

## Migration

`Phase6Inventory` was rewritten by hand to preserve data, because EF generated the `StockQuantity` drop first:
1. Create one item per default variant. On hand = the old stock + what Pending orders hold, since those orders had already decremented it.
2. Create reservations: Active for Pending orders (with a fresh 30-minute window, after which the sweeper settles them) and Committed for Paid orders that have not shipped, so that cancelling them restocks as before.
3. Attach every historical movement to its item, and add an opening-balance line wherever the history did not add up to on-hand.
4. Drop the old `Products` columns.

`Down` restores the available quantity to `Products`. `MigrationRehearsalTests` covers Pending and Paid legacy orders.

## Consequences

- **Positive:**
  - The last unit is sold once (parallel-checkout tests), and the losers get a clear `InsufficientStock`.
  - The ledger reconciles with on-hand after every step of the order lifecycle (integration-tested).
  - Abandoned checkouts release their stock.
  - A correction never overwrites a sale.
  - Every cancellation path uses one inventory contract.
- **Costs:**
  - Every checkout writes the inventory row. A flash sale on one SKU serializes on it, and retries absorb the conflicts.
  - The sweeper assumes one instance. Two instances are safe (idempotent operations, rowversion) but do redundant work; a distributed lock belongs to Phase 23.
  - Stripe intent cancellation is implemented against the documented API but was not exercised on a live account. It is reported together with P-05.
  - Low-stock "alerts" are the dashboard badge and list; alert emails arrive with the outbox in Phase 14.
- **Revisit when:**
  - one SKU's contention shows up in latency (a queue or split stock rows);
  - there are several warehouses;
  - baskets need to reserve (Phase 8 decides).
