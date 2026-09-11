# Explicit non-goals: what Souq deliberately does not use

> **What this page is for.** Every technology below is a reasonable, well-known choice that Souq **chose not to adopt**. Each entry records what it would solve, why it is not needed yet, the evidence that would justify it, what the codebase already does to keep the door open, and which ADR would have to be written first.
> **Why it exists.** Without this page, a future engineer or AI sees an "enterprise" pattern missing and adds it — and the product pays complexity forever for a problem it never had. Absence here is a decision, not an oversight.
> **Related:** [ArchitectureEvaluation.md](ArchitectureEvaluation.md) (how the architecture was chosen) · [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md) (the order in which to scale) · [ADR-0001](../11-ADR/0001-target-architecture.md), [ADR-0012](../11-ADR/0012-service-extraction-strategy.md)

**The standard for adopting any of these:** a demonstrated problem with evidence from this system — a measurement, an incident, or a requirement in writing — not a resemblance to a bigger company's architecture. "We might need it later" is what the preparation column is for.

## 1. Microservices

- **Would solve:** independent deployment and scaling per capability; team autonomy at scale.
- **Not now because:** one team, one deployment, and the hardest problem in this product — cross-module consistency at checkout (stock, coupon, order, payment) — is currently solved by one database transaction. Splitting would replace that with sagas and compensations for no business gain.
- **Evidence that would justify it:** several teams blocked on one another's releases; one capability needing a fundamentally different scaling or compliance profile (PCI isolation for Payments, flash-sale load for Inventory); a module whose failure must not touch the rest.
- **Already prepared:** modules own their tables and talk through contracts; no cross-module foreign keys except `TenantId`; reserve → commit/release contracts for Inventory and Promotions, which become saga steps unchanged; the outbox makes Notifications extractable today.
- **Requires:** a new ADR that names the service, its data, its contract, and the consistency it gives up. See [ADR-0012](../11-ADR/0012-service-extraction-strategy.md) for the extraction order.

## 2. Event sourcing

- **Would solve:** a perfect audit trail and time travel over aggregate state.
- **Not now because:** the business needs *records*, not replays. Orders already keep an immutable status history with actors, inventory keeps an append-only ledger, and platform actions are audited. Event sourcing would add projections, versioning and rebuild tooling to every read.
- **Evidence that would justify it:** a regulatory requirement to reconstruct any entity's state at any past time; or analytical demand that the current histories cannot answer.
- **Already prepared:** `OrderStatusHistories` and `StockMovements` are append-only; domain events exist for the facts that matter.
- **Requires:** an ADR covering the event store, versioning, snapshots, and how reads are rebuilt. Note that event sourcing, CQRS and Kafka are three separate decisions — adopting one does not imply the others.

## 3. Kafka or any message broker

- **Would solve:** durable fan-out to independent consumers, replay, and back-pressure between services.
- **Not now because:** there is one process. The transactional outbox already guarantees that a message written in a business transaction is delivered at least once, with bounded retries and a dead-letter state, and nothing consumes those messages except this application.
- **Evidence that would justify it:** a second independent consumer (an extracted service, a data platform, a partner integration); message volume that a database-backed outbox cannot keep up with; a need to replay a stream.
- **Already prepared:** `OutboxMessage` stores references only, with an allow-listed type registry, so the payloads are already broker-shaped; dispatch is a hosted service that could publish instead of handling.
- **Requires:** an ADR covering the broker, delivery semantics, schema versioning and operations. Adding a broker without a second consumer buys operational cost and nothing else.

## 4. A database per module, or per tenant

- **Would solve:** hard data isolation, independent scaling, noisy-neighbour containment.
- **Not now because:** one shared database with an enforced `TenantId` (filter, write guard, composite keys, isolation tests) meets the isolation requirement at near-zero cost per store, and keeps checkout atomic.
- **Evidence that would justify it:** an enterprise customer contractually requiring physical separation; a single store large enough to distort the shared workload; a compliance regime that forbids co-tenancy.
- **Already prepared:** [MultiTenancy.md](MultiTenancy.md) describes the hybrid path — the tenant directory can point one store at a dedicated connection without touching business code.
- **Requires:** an ADR covering connection routing, migrations across many databases, backup and restore per tenant, and cross-tenant reporting.

## 5. Distributed transactions (two-phase commit) and sagas

- **Would solve:** consistency across separate databases or services.
- **Not now because:** there is one database and one process, so a plain transaction is both simpler and stronger. Sagas exist only where a network call is involved (refunds: reserve → call → record, with an idempotency key).
- **Evidence that would justify it:** the first module extraction (§1), which turns one of today's in-process contracts into a network hop.
- **Already prepared:** the contracts that would need sagas are already shaped as reserve → commit/release; [ADR-0021](../11-ADR/0021-transaction-boundaries.md) forbids holding a transaction across a network call, so no code depends on that being possible.
- **Requires:** the extraction ADR itself.

## 6. CQRS with separate read stores everywhere

- **Would solve:** read/write models optimized independently; heavy reporting without touching the write path.
- **Not now because:** Souq uses **selective** CQRS already: commands go through aggregates, reads go through projection query services over the same tables. Separate stores would add synchronization lag and rebuild machinery to screens that are fast today.
- **Evidence that would justify it:** dashboards or reports that miss their latency budget with correct indexes in place; reporting queries measurably competing with checkout.
- **Already prepared:** every read already goes through a query service behind a port, so a read model can replace one implementation without touching a handler ([CQRS.md](CQRS.md)).
- **Requires:** an ADR naming the read model, its freshness guarantee and how it is rebuilt.

## 7. DDD tactical patterns everywhere

- **Would solve:** uniformity.
- **Not now because:** aggregates, value objects and domain services are used where invariants are rich (Order, InventoryItem, Coupon, Payment, Tenant, User) and skipped where they would be ceremony (categories, wishlist entries, configuration read models). Uniformity is not a business outcome ([DDD.md](../03-DOMAIN/DDD.md)).
- **Evidence that would justify more of it:** a concept growing rules that are currently enforced in handlers or in the database only.
- **Requires:** nothing formal — promote a type to an aggregate when its invariants demand it, and say so in the module document.

## 8. Kubernetes, service mesh, multi-region

- **Would solve:** orchestration, zero-downtime rollouts, traffic policy, geographic resilience.
- **Not now because:** the product deploys as a handful of containers. The application is stateless apart from local upload storage, so scaling out starts with "run more instances behind a load balancer".
- **Evidence that would justify it:** sustained load that one instance cannot serve; an availability target that needs automated failover; customers in a region with latency or residency requirements.
- **Already prepared:** stateless request handling (JWT, no session affinity), configuration through environment variables, background work that tolerates a single active instance.
- **Requires:** an ADR, and first the items in [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md) that are cheaper: indexes, caching, more instances.

## 9. A distributed cache (Redis) and read replicas

- **Would solve:** cross-instance caching (tenant configuration, catalog), read scaling.
- **Not now because:** the caches that exist (tenant directory, storefront configuration) are in-process and invalidated on write. With one instance that is correct and free; with several, a store would see stale settings for a short window.
- **Evidence that would justify it:** running more than one instance (then a shared cache or a short TTL becomes a correctness question, not an optimization), or read load that indexes cannot absorb.
- **Already prepared:** caching sits behind the directory and configuration services, so the implementation changes in one place.
- **Requires:** an ADR for the cache; read replicas additionally need a documented staleness contract for each read path.

## 10. GraphQL, gRPC, or an API gateway

- **Would solve:** flexible client queries; efficient service-to-service calls; edge policy.
- **Not now because:** one first-party frontend consumes a REST API with stable error codes and paging conventions. GraphQL would add query-cost and authorization complexity per field; gRPC has no second service to talk to.
- **Evidence that would justify it:** third-party API consumers with divergent data needs; mobile clients paying for over-fetching; a second service.
- **Requires:** an ADR, including how tenant isolation and permissions are enforced per field or per method.

## 11. Server-side rendering, a meta-framework, or micro-frontends

- **Would solve:** SEO for crawlers that do not execute JavaScript; faster first paint; independent frontend deployments.
- **Not now because:** the storefront is a single-page app whose SEO need is met by per-host head injection (PLANNED, Phase 16). Micro-frontends solve an organizational problem this team does not have.
- **Evidence that would justify it:** organic search becoming the main acquisition channel with measured losses from client rendering; several teams shipping frontend independently.
- **Already prepared:** the SPA reads its identity from the storefront configuration endpoint, so a server-rendered shell would consume the same contract.
- **Requires:** an ADR; note that it also changes hosting (a Node process next to nginx).

## 12. A search engine (Elasticsearch, OpenSearch)

- **Would solve:** relevance ranking, facets, typo tolerance, synonyms.
- **Not now because:** catalog search is SQL over a store's products, which is adequate at current catalog sizes.
- **Evidence that would justify it:** catalogs large enough that SQL search misses the latency budget, or merchants asking for relevance features SQL cannot express.
- **Already prepared:** catalog reads sit behind query services; a search port would replace one implementation.
- **Requires:** an ADR covering indexing, per-tenant isolation in the index, and rebuild.

## 13. A background-job framework (Hangfire, Quartz)

- **Would solve:** scheduling, retries, dashboards, distributed locking for recurring work.
- **Not now because:** the two recurring jobs (checkout expiry, outbox dispatch) are .NET hosted services with explicit leases and bounded retries, and they are testable by calling the use case directly.
- **Evidence that would justify it:** several instances competing on the same schedule (needs a distributed lock), many jobs, or operators needing a job dashboard.
- **Already prepared:** the outbox lease already makes concurrent dispatchers safe; jobs are thin wrappers around use cases, so the scheduler is replaceable.
- **Requires:** an ADR; note the extra storage and dependency.

## 14. Per-tenant custom code, themes or CSS injection

- **Would solve:** unlimited per-client customization.
- **Not now because:** it is the fastest way to destroy a white-label product: every client becomes a fork that must be upgraded by hand, and arbitrary CSS or scripts are an XSS vector. Customization is configuration: branding, presets, modules, content ([WhiteLabel.md](../08-FRONTEND/WhiteLabel.md)).
- **Evidence that would revisit it:** a paid tier with sandboxed extension points, designed as a product feature available to everyone.
- **Requires:** an ADR covering sandboxing, upgrade policy and support boundaries.

## 15. Replacing the ORM, or adding a second one

- **Would solve:** micro-optimized queries (Dapper), or different mapping ergonomics.
- **Not now because:** EF Core carries the tenant query filter, the write guard, concurrency tokens and migrations. A second data access path would bypass exactly the safety net that keeps stores isolated — which is why raw SQL outside migrations fails a test.
- **Evidence that would justify it:** a specific query that EF cannot express efficiently, measured, and only behind a query service with the tenant predicate written explicitly.
- **Requires:** an ADR, plus an extension of `TenancyRuleTests` to cover the new path.

## 16. Things that are deferred, not rejected

These are *not* non-goals; they are scheduled or waiting on a decision, and belong to the roadmap rather than this page:

- TypeScript and TanStack Query in the frontend (decision D-19, deferred with a trigger, [ADR-0035](../11-ADR/0035-white-label-runtime.md)).
- A CI pipeline running the existing suites (no pipeline exists today).
- Cloud blob storage for uploads, and per-store email sending domains (roadmap Phase 23).
- The platform owner's console and the store dashboard (roadmap Phases 17–18).
- A tax model (open product decision P-06) and the payment-account model (D-13).

See [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) for their phases and [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) for what their absence costs today.
