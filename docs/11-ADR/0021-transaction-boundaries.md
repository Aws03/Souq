# ADR-0021: Transaction boundaries — the use case owns the unit of work; no transaction spans a network call

- **Status:** Accepted, 2026-09-11 (Phase 1B). Documents and standardizes what Phase 1A built for checkout.
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (every command handler); Ordering, Inventory, Promotions, Payments
- **Related ADRs:** builds on [ADR-0013](0013-optimistic-concurrency.md); its compensation maps onto the saga steps of [ADR-0012](0012-service-extraction-strategy.md); followed by [ADR-0024](0024-platform-administration.md), [ADR-0026](0026-inventory-reservations.md), [ADR-0027](0027-customer-profile-and-erasure.md), [ADR-0029](0029-orders-lifecycle.md), [ADR-0030](0030-coupon-redemptions.md) and [ADR-0031](0031-payments-and-refunds.md); the side effects it defers are moved to the outbox by [ADR-0034](0034-notifications-outbox.md)

## Context

- EF Core runs every `SaveChangesAsync` in one database transaction. Handlers call it through `IUnitOfWork`.
- Checkout calls the payment provider; payment confirmation calls it too; confirmation sends an email. Phase 1A made checkout compensate a failed intent (C6) and resolved client/webhook races with `rowversion` and a re-read (ADR-0013).
- Handlers also called `Repository.Update()` on entities that were already tracked, which marks every column modified (Phase 0 D7).
- Later modules (inventory reservations, coupon redemptions, notifications) need a clear rule before they add more steps.

## Problem

Where are transaction boundaries, how are external calls combined with them, and how are failures handled?

## Options considered

| Option | Verdict |
|---|---|
| A MediatR behavior that opens a transaction around every command | Hides the boundary; would hold the transaction open while the handler calls Stripe |
| Hold one transaction across the payment call | Locks rows for the provider's latency; a slow provider blocks other checkouts |
| Distributed transaction / saga now | No second service exists — cost without benefit |
| **The command handler owns the boundary; each `SaveChangesAsync` is one atomic step; external calls happen between steps; failures are compensated or resolved idempotently** | Chosen |

## Decision

1. **The command handler is the transaction boundary.** It loads aggregates, calls domain methods and saves through `IUnitOfWork`. One consistent state change = one `SaveChangesAsync`. Work spanning several modules in one step uses the **same** unit of work (one `DbContext`), so it is atomic.
2. **No explicit `BeginTransaction` in handlers.** If a future use case needs several saves to be atomic, it gets an explicit transaction through the EF execution strategy, reviewed case by case.
3. **No database transaction is open during a network call.** Pattern: save → call provider → save.
   - *Checkout:* save the Pending order (stock reserved) → create the payment intent → save the intent id. If the provider fails, **compensate** in a new step: cancel the order and release the stock.
   - *Confirmation:* read the order → ask the provider → save Paid or Cancelled. A concurrent confirmation loses on `rowversion` and **re-reads**: already Paid is an idempotent success.
4. **Invariants are protected twice:** inside the aggregate (domain rules) and by the database (`rowversion`, unique indexes, FKs). Conflicts become 409; they are not retried silently inside the same unit of work.
5. **Side effects happen after the commit, never before.** Today the confirmation email is best-effort after the save (its failure does not fail the order). Phase 14 moves such effects to an **outbox** written in the same transaction as the change and dispatched in the background.
6. **Tracked aggregates are saved without `Update()`.** The change tracker writes only the modified columns (D7). `Update()` was removed from the repository contract.

## Why

- Short transactions keep contention low and make every step's outcome clear.
- Compensation and idempotent re-reads give correctness without distributed transactions, and they map directly onto saga steps if a module is ever extracted ([ADR-0012](0012-service-extraction-strategy.md)).

## Consequences

- A process crash between checkout's two saves can leave a Pending order without an intent. It holds stock until the expiry job of Phase 6/9 (known residual of C6).
- The confirmation email can be lost if the provider fails after commit; the outbox in Phase 14 closes this.
- SQL Server transient-fault retries (`EnableRetryOnFailure`) are not enabled yet; they depend on the deployment target and are evaluated in Phase 23.

## Revisit when

- Phase 6/9: inventory reservations and order expiry (background jobs) — they follow the same rule and add the expiry that closes the residual above.
- Phase 14: the outbox becomes mandatory for any cross-module or external side effect.
- A use case genuinely needs multi-step atomicity with an external call in the middle: consider a saga with explicit states.
