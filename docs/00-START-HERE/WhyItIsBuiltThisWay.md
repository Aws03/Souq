# Why Souq is built this way

> **What this page is:** the important "why" questions a new engineer, a new owner or a learner will ask, each answered in a few lines and linked to the record that argues it in full. It is an index into decisions, not a new source of them: where this page and an ADR differ, the ADR (and any later ADR that supersedes it) wins.
> **Level:** L3, but every answer is readable at L1. **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`.
> **Where decisions live:** architectural decisions in [ADRs](../11-ADR/README.md); rejected technologies in [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md); open commercial decisions in [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md); known risks in [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) and [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).

## Shape of the system

### Why a modular monolith?

One team, one product, one database transaction for checkout. A modular monolith has:
- one deployable;
- one database;
- 13 business modules that own their data and talk through explicit contracts.

That gives the separation that makes a later extraction possible, without paying today for network calls, distributed transactions, per-service deployments and eventual consistency where money and stock must agree. The module rules are executable tests, so the separation doesn't decay into a "big ball of mud".
→ [ADR-0001](../11-ADR/0001-target-architecture.md), [ADR-0002](../11-ADR/0002-modular-monolith-structure.md), [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md)

### Why not microservices?

Checkout must reserve stock, take a coupon use and create an order atomically. Across services that becomes a saga with compensations for every step, for a team and a load that don't need it.

The code is prepared instead of distributed:
- modules reference each other by id and snapshot what they need;
- calls that would need a saga after extraction are already designed as *reserve → commit/release* (Inventory, Promotions);
- side effects go through an outbox.

Scaling happens in a fixed order — queries and indexes, caching, more app instances, read replicas, dedicated databases for large stores — before any extraction.
→ [ADR-0012](../11-ADR/0012-service-extraction-strategy.md), [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md), [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md)

### Why not event sourcing, Kafka or a message broker?

Nothing in Souq needs to rebuild state from a history of events, and the auditable histories that matter (the stock ledger, order status history, payments and refunds, the audit log) are ordinary append-only tables. Events are used only at the edges:
- a domain event with more than one consumer;
- written to an outbox table in the same transaction;
- delivered by a background dispatcher.

That gives the useful half of event-driven design (reliable side effects after the commit) without a second source of truth or new infrastructure to run.
→ [ADR-0001](../11-ADR/0001-target-architecture.md), [ADR-0034](../11-ADR/0034-notifications-outbox.md), [Events.md](../02-ARCHITECTURE/Events.md)

### Why Clean Architecture and ports and adapters?

Business rules must outlive technology choices. The Domain references nothing, so "an unpaid order cannot ship" doesn't change when EF Core, Stripe or the email provider does. Every external system sits behind a port the core owns, and the adapter translates provider types and errors. This is enforced by `DependencyRuleTests`, not by convention.
→ [ADR-0003](../11-ADR/0003-clean-hexagonal-boundaries.md), [DependencyRules.md](../02-ARCHITECTURE/DependencyRules.md)

### Why are modules folders and namespaces, not projects?

Thirteen projects would add build and dependency ceremony without making a boundary more real than an architecture test does. Changing this is an ADR, not a refactor.
→ [ADR-0002](../11-ADR/0002-modular-monolith-structure.md)

## Data and tenancy

### Why one shared database with a `TenantId` on every row?

A database per store multiplies migrations, backups and connection pools by the number of stores. For many small stores that's the most expensive part of running the platform.

A shared database needs isolation that doesn't rely on every developer remembering a `Where`, so it is layered:
- the store is resolved from the host;
- a global query filter;
- a write guard;
- composite foreign keys;
- host-bound tokens.

Moving one large store to its own database with the same schema is recorded as a FUTURE option. The seam the ADR named was never built; the tenant directory is where a per-store connection would be chosen ([ADR index §4](../11-ADR/README.md)).
→ [ADR-0005](../11-ADR/0005-multi-tenancy-model.md), [ADR-0022](../11-ADR/0022-tenancy-enforcement.md), [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md)

### Why does the store come from the host name, and never from the request?

Anything a client sends, a client can change. A store id in a query string or body is an invitation to try the neighbour's. The host is resolved on the server, the token must match it, and no request type may carry a `TenantId` (a test enforces that). The single reviewed exception is the platform area, which names a store in its route and audits every request.
→ [ADR-0006](../11-ADR/0006-tenant-resolution.md), [ADR-0024](../11-ADR/0024-platform-administration.md)

### Why does another store's id answer 404 rather than 403?

`403` tells an attacker the id exists. With filtered repositories, another store's row simply isn't found.
→ [ADR-0022](../11-ADR/0022-tenancy-enforcement.md)

### Why optimistic concurrency (`rowversion`) instead of locks?

Most edits don't collide. When they do — two staff members on one order, two customers on the last item — the second save fails and becomes a `409` or a bounded retry, instead of silently overwriting or holding locks across a user's think time.
→ [ADR-0013](../11-ADR/0013-optimistic-concurrency.md)

### Why is time stored in UTC, and why does every API instant end in `Z`?

Stores sit in different time zones, and the server must compare "now" with expiry times, reservations and report periods consistently. Storage is UTC and clocks are injected so time-dependent rules are testable. Display converts to the store's time zone.

The `Z` suffix is the other half: without it, a browser reads a timestamp in its own zone, and every order time was shown shifted by the reader's offset until this was fixed.
→ [ADR-0007](../11-ADR/0007-database-strategy.md) (timestamps), [ApiDocumentation.md](../05-API/ApiDocumentation.md) §1

### Why `decimal(19,4)` and a `Money` value object?

Currencies have different minor units: JOD has three decimals, JPY none, most currencies two. `decimal(19,4)` holds every ISO-4217 exponent. `Money` refuses an amount its currency can't represent, and rounds calculated amounts once, in one place. A bare `decimal` with a separate currency string is how currencies get mixed and cents get lost.
→ [ADR-0014](../11-ADR/0014-money-precision.md)

## Behaviour

### Why do some business rules live in the Domain and others in handlers?

The test is: *would this rule survive replacing the database and the payment provider?*
- A rule about one aggregate's own state lives in that aggregate: "an unpaid order cannot ship", "available stock never goes negative".
- A rule that needs a lookup can't live in an aggregate, which can't query, so the handler checks it: "this slug is already taken", "a used coupon can't be deleted".
- Coordination across modules is the handler's job.

Tactical DDD is used where invariants are rich (orders, stock, coupons, payments, tenants, users). Simpler entities stay simple.
→ [ADR-0009](../11-ADR/0009-ddd-usage.md), [DDD.md](../03-DOMAIN/DDD.md), [EngineeringMentalModel.md](EngineeringMentalModel.md) §2

### Why is CQRS selective?

Commands load aggregates and save through a unit of work, because that's where invariants are protected. Reads skip aggregates and project straight into DTOs with `AsNoTracking`, because loading an order graph to display a list is wasted work.

There is no separate read database. Separate read models are reserved for reporting and search, and only if agreed latency targets are missed.
→ [ADR-0008](../11-ADR/0008-cqrs-strategy.md), [CQRS.md](../02-ARCHITECTURE/CQRS.md)

### Why is reporting done with SQL aggregation?

A dashboard that loads rows and sums them in memory costs more for every order a store takes. All aggregation happens in SQL, so a store with a hundred thousand orders costs the dashboard what a store with a hundred does.

The period is a closed key (`Today`, `Last7Days`, …) computed on the server from the injected clock. The browser can't ask for an arbitrary or expensive window. Metrics the data can't support (profit, margin, conversion) are declared absent rather than estimated.
→ [Dashboards.md](../04-MODULES/Reporting/Dashboards.md), [ADR-0008](../11-ADR/0008-cqrs-strategy.md)

### Why one pricing pipeline?

Before it, money was computed in three places — the browser cart, checkout and the coupon preview — and they could disagree. With one pipeline (subtotal → discount → shipping → tax), the basket total equals the order total by construction, and the client only displays what the server computed.
→ [ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md)

### Why is no network call made inside a database transaction?

A slow payment provider would hold locks for its whole response time, and a rollback can't take back a charge or an email. So the pattern is save → call → save, with compensation when the call fails. A paid order's refund, for example, is issued after its cancellation commits.
→ [ADR-0021](../11-ADR/0021-transaction-boundaries.md)

### Why are payment operations behind an abstraction, and why a state machine?

- **The abstraction:** the payment provider is an external system; it sits behind a port so the core never sees provider types, and a fake gateway can run the full flow in tests. It is never used implicitly outside Development/Testing.
- **Per-store gateway accounts:** they let each store be paid into its own account, with keys encrypted and bound to that store.
- **The state machine:** a payment intent's state is explicit because a real defect existed without one: a declined card could capture against an order that had already been cancelled.
→ [ADR-0031](../11-ADR/0031-payments-and-refunds.md), [ADR-0036](../11-ADR/0036-payment-intent-state-machine.md)

### Why an outbox for email and notifications?

A request that sends email directly either waits on the provider or sends mail for a change that later rolled back. Writing a message row in the same transaction as the change, and delivering it from a background dispatcher with a lease and bounded retries, means nothing is lost on a crash and no request waits.
→ [ADR-0034](../11-ADR/0034-notifications-outbox.md), [Events.md](../02-ARCHITECTURE/Events.md)

## Frontend

### Why one React build for every store?

A build per client is a fork per client. The storefront boots from `GET /api/storefront/config` for its host, and that answer decides:
- name, colours, fonts, dark mode;
- languages, currency;
- enabled modules and SEO text.

Tests forbid brand and currency literals in the code.
→ [ADR-0035](../11-ADR/0035-white-label-runtime.md), [WhiteLabel.md](../08-FRONTEND/WhiteLabel.md)

### Why is the frontend never authoritative?

Anything in a browser can be changed by its user. Guards, hidden buttons and disabled menus are user experience; prices, stock, permissions, tenancy and module availability are decided and re-checked on the server.
→ [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md), [ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md) (the query cache is never the authority)

### Why TanStack Query and JSDoc type-checking rather than TypeScript?

Hand-written fetching had repeated cancellation and stale-state defects, so a query library was adopted, with keys in one module and a reset on identity change.

Type-checking the `.js` boundaries with JSDoc catches the errors that mattered at a fraction of a conversion's cost. A full TypeScript conversion was evaluated and not adopted, with the conditions that would reopen it written down.
→ [ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md), [ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md)

## What is deliberately unfinished

### Why is the storefront preview waiting for the owner instead of being built?

A preview token is a new credential that lets a request past the store-status gate, on another host, through a new public endpoint. Who may hold one, which store states it opens, whether it is read-only, how long it lives and how it crosses hosts are security decisions with real consequences. For example, a suspended store may have been closed for billing or abuse.

The repository records no rule for any of them, and `AGENTS.md` §9 says to stop there. The options and a recommendation are written up for decision D-22.
→ [StorefrontPreview.md](../04-MODULES/Platform/StorefrontPreview.md), [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md)

### Why is there no platform settings screen?

The permission `platform.settings.manage` exists, but no platform-wide setting is defined. A screen would have nothing true to edit, and choosing what belongs there is a product decision (P-07).
→ [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md)

### Why do some risks remain open?

Some remaining risks are engineering work that hasn't been done yet. Examples: least-privilege database logins in a real deployment, TLS, scheduled and off-site backups.

Others can't be closed by engineering at all:
- whether Stripe treats JOD as two or three decimals needs one charge on the real account (P-05);
- tax needs a commercial decision (P-06);
- whether a duplicate checkout should replay or be refused is a customer-experience choice (F-8);
- whether a paid order can be refunded by someone who only manages orders is a policy choice (R-03);
- the licence is a legal decision (P-03).

Engineering records these with their evidence instead of guessing an answer that would look deliberate.
→ [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md), [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md), [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md), [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md)

### Why does Phase 16 stay open?

It doesn't: Phase 16 closed with V3 ([ADR-0041](../11-ADR/0041-storefront-variant-selection.md)). The whole variant path is built — an order records the exact variant bought, merchants define options and manage variants, and shoppers choose one explicitly with sold-out values disabled and "From" pricing in lists ([ADR-0039](../11-ADR/0039-product-variants-order-identity.md), [ADR-0040](../11-ADR/0040-product-option-model.md), [ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md)). What remains is V4: reporting per variant and relabelling the dashboard's stock figures, which now count variant rows.
→ [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) (Phase 16), [ADR-0025](../11-ADR/0025-catalog-model.md)
