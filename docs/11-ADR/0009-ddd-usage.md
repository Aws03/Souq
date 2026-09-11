# ADR-0009: Where DDD is used

- **Status:** Accepted, 2026-09-11
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (domain modelling in every module)
- **Related ADRs:** builds on [ADR-0001](0001-target-architecture.md); boundaries from [ADR-0004](0004-module-boundaries.md); applied by [ADR-0025](0025-catalog-model.md) (the product aggregate) and [ADR-0026](0026-inventory-reservations.md) (the inventory aggregate); the domain events it defers arrive with [ADR-0034](0034-notifications-outbox.md)

## Context

The Domain already uses rich entities (`Order` with a guarded state machine and status history, `Product` with guarded stock, `Coupon`, `Money` as a value object). Upcoming modules range from invariant-heavy (inventory reservations, payments, tenant lifecycle) to near-CRUD (wishlist, categories).

## Problem

How much DDD should be applied, and where? And how do we stop aggregates from growing into "everything connected to an order"?

## Options considered

1. DDD tactical patterns everywhere (every entity an aggregate with events and repositories).
2. No tactical DDD (anemic entities, logic in services).
3. **Selective:** strategic DDD for boundaries; tactical patterns where invariants are rich; simple models elsewhere.

## Decision

**Option 3.**
- **Rich aggregates:** Tenant, User, Product (+ variants), InventoryItem, Order, Coupon, Payment, Customer.
- **Simple:** Category, WishlistItem, settings read models, Review (small rules only).
- **Aggregate rules:**
  - Keep them **small**.
  - Reference other aggregates **by id**.
  - One aggregate changes per command, *except* the checkout use case, which coordinates module contracts in one transaction by design ([ADR-0004](0004-module-boundaries.md)).
- **Order** contains only its lines and status history. Payments, shipments, reviews, and the customer are separate aggregates.
- **Value objects** when they carry rules: `Money` (minor units, arithmetic), `Address`, `Slug`, `Email`, `Sku`.
- **Domain services** only for I/O-free rules spanning aggregates, such as a pricing calculator.
- **Domain events** introduced with the outbox (Phase 14), and only for facts with more than one consumer.

## Why

- Tactical DDD pays for itself where a violated invariant costs money or trust (stock, payments, order state).
- On CRUD it is pure ceremony.
- Small aggregates keep concurrency conflicts rare and transactions short.

## Consequences

- New rules go into the owning aggregate or value object, with unit tests (the CLAUDE.md rule).
- Cross-aggregate rules that need lookups stay in Application handlers.
- An aggregate that keeps attracting unrelated fields is a design smell: split it.

## Revisit when

- A "simple" module accumulates invariants (for example, reviews with complex moderation): promote it to a rich model.
