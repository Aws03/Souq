# ADR-0004: Module boundaries and inter-module communication

- **Status:** Accepted, 2026-09-11
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (the 13 modules and the rules between them): Platform, Identity, Catalog, Inventory, Customers, Shopping, Ordering, Payments, Promotions, Shipping, Reviews, Notifications, Reporting
- **Related ADRs:** builds on [ADR-0001](0001-target-architecture.md); given physical form by [ADR-0002](0002-modular-monolith-structure.md); extraction rules in [ADR-0012](0012-service-extraction-strategy.md); contracts added by [ADR-0026](0026-inventory-reservations.md) (Inventory), [ADR-0028](0028-basket-and-pricing-pipeline.md) (pricing and basket checkout), [ADR-0030](0030-coupon-redemptions.md) (coupon redemptions), [ADR-0031](0031-payments-and-refunds.md) (order payments) and [ADR-0032](0032-shipping-methods.md) (shipping rates); the remaining cross-module repository reads are recorded by [ADR-0027](0027-customer-profile-and-erasure.md) and [ADR-0033](0033-review-moderation-and-wishlist.md)

## Context

The brief listed 16 candidate areas. Today, Ordering mutates Catalog entities directly (`product.DecreaseStock` inside `CreateOrderHandler`), and Reviews reads orders through `IOrderRepository`. That is acceptable while small, but it is the path to a ball of mud.

## Problem

Which modules exist? What does each own? How may they interact without losing checkout's transactional correctness?

## Options considered

- **Modules per entity** (Products, Categories, Baskets, Wishlists …): many tiny modules with chatty coupling.
- **Modules per capability**, with synchronous in-process contracts and a shared transaction where needed.
- **Modules per capability, communicating only through asynchronous events.** Every cross-module step becomes eventually consistent, including checkout.

## Decision

- **13 capability modules** ([Modules.md](../04-MODULES/Modules.md)): Platform, Identity, Catalog, Inventory, Customers, Shopping, Ordering, Payments, Promotions, Shipping, Reviews, Notifications, Reporting.
- Categories merge into Catalog. Basket and Wishlist merge into Shopping.
- Platform Administration and Storefront are **API/UI areas, not modules**.
- **Communication rules:**
  1. Call another module only through its `Contracts` (interfaces + DTOs).
  2. Reference other modules' data **by ID** and snapshot what you need. No cross-module FKs except `TenantId`.
  3. Several modules **may share one database transaction** in a single use case (checkout), and each writes only its own tables.
  4. Side effects with multiple consumers go through events and an outbox (from Phase 14).
  5. The dependency graph is acyclic (Payments never calls Ordering).
  6. Reporting may *read* other modules' tables through dedicated query services (a documented exception). It never writes.

## Why

- Capability boundaries match how the business thinks and changes.
- The shared transaction keeps money and stock consistent without sagas, which is the main advantage of a monolith over microservices.
- Reference by ID plus snapshots is exactly what extraction later requires.

## Consequences

- Existing cross-module calls are migrated as their modules are rebuilt: Inventory contract in Phase 6, `IOrderHistory` for Reviews in Phase 13, best-selling ranking via a query contract in Phase 5.
- New code must follow the rules immediately (code review plus architecture tests once the namespaces exist).

## Revisit when

- A module needs to be extracted. Replace its contract calls with messages and a saga ([ADR-0012](0012-service-extraction-strategy.md)).
- Two modules keep changing together. That means they are really one module, so merge them.
