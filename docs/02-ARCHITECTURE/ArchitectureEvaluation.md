# Architecture Styles: Evaluation for Souq

> **Purpose:** choose the *smallest* architecture that gives Souq strong long-term boundaries without premature complexity.
> **Decision recorded in:** [ADR-0001](../11-ADR/0001-target-architecture.md). **Target described in:** [Architecture.md](Architecture.md).
> **Date:** 2026-09-11

## 0. First, a category error to avoid

These ten "architectures" answer **different questions**, so most of them are not alternatives to each other:

| Question | Styles that answer it |
|---|---|
| How many deployable units run in production? | Monolith · Modular monolith · Microservices |
| Which way do source-code dependencies point? | Layered · Clean · Hexagonal |
| How is the business split into cohesive areas? | Modular monolith (modules) · DDD (bounded contexts) |
| How is code organized around use cases? | Vertical slices |
| How are business rules modeled? | DDD (tactical patterns) |
| Are reads and writes modeled separately? | CQRS |
| How do parts of the system learn that something happened? | Event-driven architecture |
| What is the source of truth for state? | Event sourcing (events) vs state-based persistence |

A system can be a *modular monolith* (deployment) that follows the *Clean* dependency rule, uses *ports and adapters* for integrations, is split into *DDD-informed modules*, organizes use cases as *slices*, and applies *CQRS* where reads differ from writes. That is the combination evaluated below.

**The product context that drives every judgement:**
- One developer today; a small team later.
- About 16 business capabilities.
- Many small tenants (≈ $5k each), so operating cost per tenant must stay near zero.
- SQL Server + EF Core + .NET 10 + React.
- Correctness-critical flows: checkout, inventory, payments.
- An existing, well-built layered Clean Architecture codebase with 133 tests.

---

## 1. Traditional layered monolith (UI → Business → Data)

- **Solves:** basic separation of presentation, logic, and data access. Easy to learn.
- **Introduces:** business logic depends *on* data access. Models become database-shaped and anemic, logic leaks into services and controllers, and the core can't be tested without the database.
- **Operational complexity:** lowest (one app, one DB).
- **Development complexity:** low at first, rising fast as features couple through shared services and tables.
- **Testing:** mostly integration tests, because the logic is bound to the data layer.
- **Database:** one schema shared by everything. No ownership.
- **Multi-tenancy:** possible, but tenant filtering tends to be re-implemented in each service. That is fragile.
- **Team size:** fine for 1–3 people, then merge conflicts and hidden coupling grow.
- **Deployment and scaling:** one unit, scaled horizontally as a whole.
- **Now?** ❌ Souq is already past this. Its Domain doesn't depend on data access, and going back would be a regression.
- **Later?** ❌ Clean Architecture is its strict successor.

## 2. Clean Architecture

- **Solves:**
  - Dependencies point inward: API → Infrastructure → Application → Domain.
  - Business rules are independent of frameworks, the database, and the UI.
  - Technology can be swapped (Stripe → another gateway) without touching rules.
  - The core is unit-testable.
- **Introduces:**
  - Ceremony: DTO mapping, interfaces at boundaries, more projects.
  - The common failure mode is over-abstraction (generic repositories, pass-through services).
- **Operational complexity:** none added (it is a source-code rule).
- **Development complexity:** moderate. It needs discipline about where code goes.
- **Testing:** excellent. Domain and Application can be tested with no infrastructure (the current 133 tests prove it).
- **Database:** isolated behind Infrastructure. EF configurations stay out of the Domain.
- **Multi-tenancy:** a natural fit. Tenant context becomes a port (`ITenantContext`) and enforcement lives in Infrastructure, in one place.
- **Team size:** scales well because layers are compile-time boundaries.
- **Deployment and scaling:** neutral.
- **Now?** ✅ Already in place and working. Keep it as the **dependency rule**.
- **Later?** ✅ Still valid inside each module, or inside each extracted service.

## 3. Hexagonal architecture (ports and adapters)

- **Solves:**
  - The same inward dependency as Clean, stated as *ports* (interfaces owned by the core) and *adapters* (implementations at the edge).
  - It treats driving adapters (HTTP, webhooks, background jobs, CLI) and driven adapters (DB, Stripe, email, storage) symmetrically.
- **Introduces:** vocabulary and indirection. If every class gets a port, it becomes abstraction for its own sake.
- **Operational and development complexity:** low, if ports exist only at real variation points.
- **Testing:** fakes for driven ports (e.g. `FakeGateway` for payments, `CapturingEmailSender` in tests). Driving adapters stay thin.
- **Database:** the database is just one driven adapter.
- **Multi-tenancy:** tenant resolution is a driving concern (from the host header); tenant storage and routing is a driven one. Both are clean adapters.
- **Team size, deployment, scaling:** neutral.
- **Now?** ✅ As a **principle**, not as an extra folder structure.
  - Souq already has ports: `IPaymentService`, `IEmailSender`, `IFileStorage`, `IPasswordHasher`, `IJwtTokenGenerator`.
  - The Phase 0 finding D1 (Stripe SDK used in a controller) is exactly a hexagonal violation.
- **Later?** ✅ Every future integration (shipping carriers, SMS, search, tax) follows it.

## 4. Modular monolith

- **Solves:**
  - One deployable unit, internally divided into **business modules**. Each module owns its data and rules and exposes explicit contracts.
  - It stops the ball of mud as the product grows toward 16 capabilities.
  - Several developers can work in parallel.
  - It keeps an **exit path** if a module ever has to become a service.
- **Introduces:**
  - Contracts must be designed.
  - Modules reference each other by ID rather than navigation properties.
  - Discipline about data ownership.
  - Some duplication, such as order lines snapshotting product data.
  - Boundary enforcement: architecture tests, or separate projects.
- **Operational complexity:** the same as a monolith. One pipeline, one runtime, one database, one set of logs.
- **Development complexity:** moderate. The boundaries need design thinking up front.
- **Testing:** modules can be tested alone. Cross-module flows run in-process, so they stay fast and deterministic.
- **Database:**
  - One database, with **table ownership per module**.
  - Cross-module transactions remain possible, and they matter a lot: checkout reserves stock and creates the order atomically.
- **Multi-tenancy:** one shared mechanism (query filters plus a write guard) serves every module.
- **Team size:** good from 1 to about 15 developers.
- **Deployment:** a single artifact. A release is all-or-nothing, which is acceptable for this product.
- **Scaling:** horizontal scaling of the whole app. The database scales vertically first, then with read replicas.
- **Now?** ✅ **Yes.** The right deployment and runtime shape for a young SaaS product with one team.
- **Later?** ✅ It remains the default. Individual modules can be extracted when evidence appears (see §5 and [Architecture.md §9](Architecture.md#9-future-scaling-and-service-extraction)).

## 5. Microservices

- **Solves:**
  - Independent deployment and scaling per service.
  - Fault isolation.
  - Team autonomy.
  - A choice of technology per service.
- **Introduces:**
  - A distributed system. Network failures, retries, and timeouts become normal.
  - Eventual consistency. Checkout (inventory + order + coupon + payment) turns into a saga with compensations.
  - Distributed tracing becomes mandatory.
  - Contract versioning between services.
  - N pipelines, N databases, N backups.
  - Tenant isolation re-implemented in every service.
  - Local development needs many containers.
- **Operational complexity:** very high (orchestration, a service mesh or gateway, message brokers, observability).
- **Development complexity:** high. Most of the effort goes into plumbing, not features.
- **Testing:** contract tests plus end-to-end environments. Integration tests become slow and flaky.
- **Database:** one per service. No cross-service joins, so reporting needs pipelines.
- **Multi-tenancy:** every service must resolve, propagate, and enforce the tenant, which multiplies the risk of a leak.
- **Team size:** justified with several independent teams. With one developer it is actively harmful.
- **Deployment and scaling:** fine-grained, but only valuable when load profiles really differ.
- **Now?** ❌ **No.** None of its benefits are needed today, and all of its costs would slow the product.
- **Later?** ⚠️ Possibly for a few edge capabilities with distinct scaling or reliability profiles: notifications, search, media processing, reporting. Only with evidence. See the extraction strategy.

## 6. Vertical slice architecture

- **Solves:**
  - Organizes code **by use case**: request, handler, validation, and response together.
  - A change touches one slice instead of five layers ("shotgun surgery").
  - Each slice can pick the simplest implementation that fits.
- **Introduces:**
  - Business rules can get duplicated across slices when there is no rich domain model.
  - A pure-slice codebase can lose its shared core and its consistent conventions.
- **Operational complexity:** none.
- **Development complexity:** low. Files are easy to find.
- **Testing:** one test class per slice. Handlers are small.
- **Database:** neutral. Slices may use whatever access fits: repositories for writes, projections for reads.
- **Multi-tenancy:** neutral, *provided* tenant enforcement is central rather than per slice.
- **Team size:** good. Slices rarely conflict.
- **Deployment and scaling:** neutral.
- **Now?** ✅ **As the organization of the Application layer and the frontend.** The codebase already does this (`Features/Orders/Commands/CreateOrder…`). We keep it and make the module the top-level folder.
- **Later?** ✅ It stays compatible with extraction, because slices move together with their module.

## 7. Domain-Driven Design

- **Solves:**
  - *Strategic* DDD: bounded contexts and a ubiquitous language guide the module boundaries.
  - *Tactical* DDD: aggregates, value objects, and domain events keep complex invariants consistent. For example, a paid order can't gain lines, stock never goes negative, a discount never exceeds the subtotal.
- **Introduces:**
  - A learning curve.
  - The risk of over-modeling simple CRUD (a wishlist does not need an aggregate with domain events).
  - Cargo-cult terminology.
- **Operational complexity:** none.
- **Development complexity:** moderate where applied, zero where not.
- **Testing:** excellent. Invariants are unit-tested on plain objects (70 Domain tests today).
- **Database:**
  - Aggregates define transaction and consistency boundaries.
  - Other aggregates are referenced by ID.
  - Aggregates are the natural owners of concurrency tokens.
- **Multi-tenancy:** `Tenant` is an aggregate in the Platform context, and every tenant-owned aggregate carries `TenantId` as part of its identity.
- **Team size:** helps teams communicate through a shared language.
- **Deployment and scaling:** bounded contexts are the future service seams.
- **Now?** ✅ **Selectively.** Use the rich tactical model for Ordering, Inventory, Promotions, Payments, Tenant lifecycle, and Identity. Keep Categories, Wishlist, Reviews, and store settings simple. Use strategic DDD for module boundaries.
- **Later?** ✅ Deepen it where complexity grows (pricing rules, returns, fulfilment).

## 8. CQRS

CQRS comes in increasingly expensive levels:
- **L1** separates command and query objects and handlers, over one database. MediatR does this today.
- **L2** adds dedicated read models and projections: denormalized tables or views.
- **L3** uses separate read stores (a replica or a search index) that are eventually consistent.

- **Solves:**
  - Reads and writes have different shapes. The storefront catalog, admin listings, and dashboards want flat, filtered, paged projections; writes want aggregates that protect invariants.
  - Each side can be optimized independently.
- **Introduces:**
  - L1: a little ceremony.
  - L2/L3: duplicated models, synchronization, and eventual consistency.
- **Operational complexity:** L1 none. L2 low. L3 medium to high (sync jobs, a search cluster).
- **Development complexity:** L1 low. L2 moderate. L3 high.
- **Testing:** the query side is tested against a real database (projections are SQL behaviour).
- **Database:** L1 and L2 stay in SQL Server. L3 adds stores.
- **Multi-tenancy:** every read model must carry `TenantId` and use the same filters.
- **Team size, deployment, scaling:** L3 lets reads scale independently.
- **Now?** ✅ **L1 everywhere it already exists.** Add read-side **query services that project straight into DTOs** (fixing the over-fetching found in D3). ❌ No separate stores.
- **Later?**
  - ✅ L2 for reporting and dashboards when aggregate queries over orders become slow.
  - ⚠️ L3 (a search index) only if catalog search needs relevance, facets, or scale beyond SQL.

## 9. Event-driven architecture

- **Solves:** decouples a cause from its side effects. For example: "Order paid" → send the email, update statistics, notify the admin. It is also the natural message boundary if a module is ever extracted.
- **Introduces:**
  - Eventual consistency.
  - Harder debugging.
  - Handlers must be **idempotent**.
  - Ordering and retry concerns.
  - With a broker: infrastructure to operate.
- **Operational complexity:** in-process events add none. An outbox table plus a background dispatcher adds little. A message broker adds medium to high.
- **Development complexity:** moderate. The "what happens next?" logic is no longer in one method.
- **Testing:** event handlers are unit-tested. Outbox dispatch is tested with integration tests.
- **Database:** an outbox table written in the same transaction as the change, which guarantees "no event without the change, and no change without the event".
- **Multi-tenancy:** every event carries `TenantId`, and every handler restores the tenant context before running.
- **Team size, deployment, scaling:** enables asynchronous processing and later extraction.
- **Now?**
  - ⚠️ **Only at the edges and when needed.** Checkout keeps its synchronous, transactional core, because correctness beats decoupling.
  - Notifications move to outbox-driven processing in Phase 14.
  - In-process domain events are introduced when a second consumer of the same fact appears. Until then, a direct call is simpler.
- **Later?** ✅ Outbox messages become broker messages if a module (e.g. Notifications) is extracted.

## 10. Event sourcing

- **Solves:** the full history is the source of truth. It enables temporal queries ("what did the cart look like at 10:02?") and rebuilding projections.
- **Introduces:**
  - A very high learning and operational cost.
  - Event schema versioning, snapshots, and projections for every read.
  - Hard deletion for privacy (GDPR) becomes difficult.
  - The tooling (event stores) is unfamiliar.
  - Simple questions ("current stock?") need a projection.
- **Operational complexity:** high.
- **Development complexity:** very high.
- **Testing:** clean, given/when/then tests on events, but the projections need extensive testing.
- **Database:** an append-only event store plus read projections.
- **Multi-tenancy:** every stream is tenant-scoped, and projections repeat the isolation work.
- **Team size:** demands experienced people.
- **Deployment and scaling:** neutral to complex.
- **Now?** ❌ **No.** Souq's real history needs are already met, more cheaply:
  - an append-only **stock ledger** (`StockMovements`);
  - the **order status history**;
  - an **audit log** (planned).

  These give auditability without making events the source of truth.
- **Later?** ❌ Unlikely. Revisit only if a regulator or partner requires reconstructable financial state beyond what ledgers and the audit log provide.

---

## Verdict

| Style | Role in Souq | Status |
|---|---|---|
| Layered monolith | — | Rejected (already surpassed) |
| **Clean Architecture** | Dependency rule for all code | ✅ Keep |
| **Hexagonal** | Ports at real integration points; thin driving adapters | ✅ Principle |
| **Modular monolith** | Deployment and runtime shape; business modules with owned data | ✅ Adopt |
| Microservices | — | ❌ Now; ⚠️ selective extraction later with evidence |
| **Vertical slices** | Organization of use cases (backend Application + frontend features) | ✅ Keep and formalize |
| **DDD** | Strategic for boundaries; tactical only where invariants are rich | ✅ Selective |
| **CQRS** | L1 + projection-based query services now; L2 read models for reporting later | ✅ Selective |
| Event-driven | Outbox for side effects (Phase 14); in-process events when a second consumer appears | ⚠️ Edges only |
| Event sourcing | — | ❌ Rejected (ledgers + audit log instead) |

**Is the proposed combination right?** Yes, the brief's proposal holds up: a modular monolith with Clean Architecture principles, hexagonal ports and adapters, DDD where it adds value, vertical/feature organization, and selective CQRS. I considered two simpler alternatives and rejected both:
- **"Plain Clean Architecture, no modules."** Cheaper this month, but with 16 capabilities in one Application project, cross-feature coupling (for example Ordering mutating Catalog entities directly, which happens today) grows unchecked, and later extraction becomes a rewrite.
- **"Vertical slices only, no layers."** Less ceremony, but it would discard working compile-time layer boundaries and invite business rules to be duplicated across slices.

The one refinement I add: **modules are enforced by architecture tests over namespaces inside the existing four layer projects**, not by a separate project per module. The reasons, and the conditions for revisiting this, are in [ADR-0002](../11-ADR/0002-modular-monolith-structure.md).
