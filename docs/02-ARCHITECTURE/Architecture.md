# Souq: Target Architecture

> **Status:** Adopted 2026-09-11 ([ADR-0001](../11-ADR/0001-target-architecture.md)). This document describes the architecture that carries Souq through every remaining phase. Where the current code differs, the "Today" notes say so, along with the phase that closes the gap.
> **Related:** [ArchitectureEvaluation.md](ArchitectureEvaluation.md) (why this and not the alternatives) · [Modules.md](../04-MODULES/Modules.md) · [MultiTenancy.md](MultiTenancy.md) · [DatabaseDesign.md](../06-DATABASE/DatabaseDesign.md) · [ApiDocumentation.md](../05-API/ApiDocumentation.md) · [Security.md](../07-SECURITY/Security.md) · [FrontendArchitecture.md](../08-FRONTEND/FrontendArchitecture.md) · [WhiteLabel.md](../08-FRONTEND/WhiteLabel.md) · [adr/](../11-ADR/)

## 1. Decision in one paragraph

Souq is a **modular monolith**: one deployable ASP.NET Core application plus one SQL Server database, split internally into **business modules** that own their data and talk to each other only through explicit contracts. Inside it, source dependencies follow the **Clean Architecture** rule (API → Infrastructure → Application → Domain). External systems sit behind **ports** defined by the core and implemented by **adapters** (hexagonal). Use cases are organized as **vertical slices** grouped by module. **DDD** tactical patterns are used only where invariants are rich. **CQRS** means separate command and query handlers over one database, with read-side projections, and nothing heavier until reporting needs it.

## 2. Seven words, seven different questions

| Concept | Kind of decision | In Souq |
|---|---|---|
| **Monolith vs microservices** | *Deployment/runtime*: how many processes and databases run in production | One process, one database (modular monolith) |
| **Clean Architecture** | *Source dependency direction*: who may reference whom | API → Infrastructure → Application → Domain, enforced by project references + architecture tests |
| **Hexagonal (ports and adapters)** | *Boundary to the outside world*: how the core talks to technology | Ports (interfaces) in Application; adapters in Infrastructure (driven) and API (driving) |
| **Modular architecture** | *Business decomposition*: which capability owns which rules and data | 14 modules, all implemented; `ModuleMap` (`tests/Souq.ArchitectureTests/ModuleMap.cs`) maps their feature folders ([Modules.md](../04-MODULES/Modules.md)) |
| **Vertical slices** | *Code organization*: where one use case's code lives | `Features/<Module>/<UseCase>` (command/query + handler + validator + DTO) |
| **DDD** | *Modeling discipline*: how business rules are expressed | Aggregates and value objects where invariants justify them; no domain services today ([DDD.md](../03-DOMAIN/DDD.md)) |
| **CQRS** | *Read/write separation*: are the read and write models the same? | Separate handlers; writes through aggregates, reads through projections |

They are complementary layers of one design, not rival options. A common beginner mistake is to "choose between Clean Architecture and microservices". One is about arrows in the code, the other is about processes on servers.

## 3. Layers and their responsibilities

```
src/
├── Souq.Domain          business model: entities, value objects, domain events, domain exceptions,
│                        repository ports for aggregates. References NOTHING.
├── Souq.Application     use cases (commands/queries), validation, orchestration, ports for technology
│                        (payment, email, storage, clock, current user, tenant context), DTOs.
│                        References Domain + MediatR + FluentValidation only.
├── Souq.Infrastructure  adapters: EF Core (DbContext, configurations, migrations, repositories, query
│                        services), Stripe, email providers, file storage, JWT, hashing, background jobs.
│                        References Application (and, through it, Domain).
└── Souq.API             driving adapter + composition root: controllers, middleware, auth policies,
                         Program.cs. References Application + Infrastructure (the latter only to wire DI).
```

| Put it in… | If it is… | Never put it there if it is… |
|---|---|---|
| **Domain** | A rule that is true regardless of UI or storage: "a shipped order can't be cancelled", "stock can't go negative", "discount ≤ subtotal", money arithmetic | Anything needing I/O, EF attributes, HTTP types, logging, the current time read directly |
| **Application** | Orchestrating one use case: load aggregates, call domain methods, persist, trigger side effects. Rules that span aggregates and need lookups (slug uniqueness, verified purchase) | SQL/EF queries, HTTP status codes, provider SDKs |
| **Infrastructure** | How something is done with a technology: SQL, EF mapping, Stripe calls, SMTP/HTTP email, disk or blob storage | Business decisions ("should this coupon apply?") |
| **API** | HTTP translation: routes, binding, auth attributes, Result → status code, file-upload shape checks | Business rules, database access, provider SDK calls |

## 4. Physical structure of modules

**Decision ([ADR-0002](../11-ADR/0002-modular-monolith-structure.md)):** modules are **namespaces that cut across the four existing layer projects**. They are not separate projects per module.

```
Souq.Domain/…                                   organized by kind (Entities, Interfaces, ValueObjects…); only Identity and Platform per module
Souq.Application/Features/<Folder>/<UseCase>/…  e.g. Features/Orders (module Ordering), mapped by ModuleMap
Souq.Application/Features/<Folder>/Contracts/   the module's public, in-process API for other modules
Souq.Infrastructure/<Area>/…                    by technical concern, e.g. Persistence/Configurations, BackgroundJobs
Souq.API/Controllers/…                          one flat folder
```

- **Why:**
  - Zero churn for working code.
  - The compiler keeps enforcing the *layer* rule, which is the rule a learning team breaks most often.
  - Architecture tests enforce the *module* rule.
  - With 14 modules, project-per-module would mean 50 or more projects for one developer.
- **Revisit when:**
  - more than 3–4 developers work in parallel;
  - a module becomes a concrete extraction candidate;
  - or the architecture tests keep catching the same boundary violations.

  Moving to *Souq.Modules.X* projects would then start with giving the Domain per-module namespaces, which it mostly does not have yet.
- **Today:** only the Application layer is organized per module. Its 18 feature folders map to the 14 modules in `ModuleMap` (`tests/Souq.ArchitectureTests/ModuleMap.cs`), the single source shared by the boundary tests and the generated inventories; [Modules.md](../04-MODULES/Modules.md) names each module's folders.

  The planned per-module namespaces in the other layers were never introduced. Most entities still live in `Souq.Domain.Entities` (only `Souq.Domain.Identity` and `Souq.Domain.Platform` are per module), repository ports share `Souq.Domain.Interfaces`, Infrastructure is organized by technical concern, and the controllers sit flat in `src/Souq.API/Controllers/`. What that leaves unenforced, and how it is counted instead, is in [DependencyRules.md §5](DependencyRules.md#5-what-is-not-enforced-and-why-it-matters).

## 5. Dependency rules

```mermaid
flowchart TB
    subgraph API["Souq.API (driving adapters + composition root)"]
        C["Controllers / Middleware / Policies"]
    end
    subgraph INF["Souq.Infrastructure (driven adapters)"]
        EF["EF Core: DbContext, repositories, query services"]
        EXT["Stripe / Email / Storage / JWT / Hashing"]
    end
    subgraph APP["Souq.Application"]
        UC["Use cases: commands and queries"]
        PORTS["Ports: IPaymentService, IEmailSender, IFileStorage, ITenantContext, ICurrentUser"]
    end
    subgraph DOM["Souq.Domain"]
        M["Aggregates, value objects, domain services, repository ports"]
    end
    C -->|"sends commands/queries via MediatR"| UC
    C -.->|"DI wiring only (Program.cs)"| INF
    UC --> M
    UC --> PORTS
    EF -->|implements| M
    EF -->|implements| PORTS
    EXT -->|implements| PORTS
```

### Allowed

| From | May depend on |
|---|---|
| Domain | .NET base library only |
| Application | Domain; MediatR; FluentValidation; `Microsoft.Extensions.*.Abstractions` |
| Infrastructure | Application, Domain, EF Core, provider SDKs (Stripe, MailKit…), `Microsoft.Extensions.*` |
| API | Application (commands, queries, DTOs, ports for auth-related reads); Infrastructure **only in `Program.cs`/composition code** |
| Module A (Application) | Module B's `Contracts` namespace (in-process interfaces + DTOs); Module B's **IDs** |
| Frontend | The HTTP API only |

### Forbidden (each is enforced or planned in architecture tests)

| Rule | Why | Enforced by |
|---|---|---|
| Domain → Application/Infrastructure/API, EF Core, ASP.NET, MediatR | The core must be framework-free and unit-testable | `Souq.ArchitectureTests` ✅ |
| Application → Infrastructure/API, EF Core, ASP.NET Core, Stripe, MailKit | Use cases must not know technology | `Souq.ArchitectureTests` ✅ |
| Infrastructure → API | Adapters must not know the delivery mechanism | `Souq.ArchitectureTests` ✅ |
| Controllers → repositories, `DbContext`, EF Core, provider SDKs | No database access or business logic in controllers | `Souq.ArchitectureTests` ✅ |
| Domain entities in API contracts | Entities are not DTOs: exposing them leaks internals and freezes the model | `Souq.ArchitectureTests` ✅ (no entity reachable from any request or response contract, 1B) |
| Module A → Module B's entities, repositories, or tables | Keeps modules independently changeable and extractable | **Partly.** `ModuleAndContractRuleTests` enforces it between `Souq.Application.Features` namespaces only. Domain repositories and entities live in shared namespaces, so crossings there compile — they are counted instead, in [ModuleDomainDependencies.md](ModuleDomainDependencies.md), and a new one fails the build |
| `IQueryable` crossing the Application or Domain surface | SQL would be built outside Infrastructure, bypassing paging limits and (Phase 2) tenant filters | `Souq.ArchitectureTests` ✅ (1B) |
| A `TenantId` in a client-bindable command or query | The tenant is decided by the server, never the client | `Souq.ArchitectureTests` ✅ — a tripwire for Phase 2 (1B) |
| Reading the clock directly (`DateTime.UtcNow`) | Time-dependent rules must be testable with a fixed clock | `Souq.ArchitectureTests` ✅ (IL scan, 1B) |
| Controllers reading claims or deciding ownership | Identity comes from `ICurrentUser`; ownership is a use-case rule | `Souq.ArchitectureTests` ✅ (1B) |
| React pages → business rules (prices, stock, permissions) | The backend is authoritative; the UI only reflects decisions | Code review; server tests prove enforcement |
| New generic `IRepository<T>`/`IService<T>` abstractions without a demonstrated second use | Pattern cargo-cult | Code review (documented in DevelopmentGuide) |

> **Note on the existing `IRepository<T>`.** It stays (it works and is small), but specialized repositories are the norm. New aggregates get exactly the repository methods they need. Reads go through query services (§8), not through repository methods.

## 6. How modules talk to each other

1. **Synchronous, in-process contracts (the default).**
   - Module B exposes an interface in `Features/B/Contracts`, for example `IInventoryReservations.ReserveAsync(lines)`.
   - Module A calls it. B's implementation touches only B's tables.
2. **The same database transaction is allowed.** Checkout must be atomic: reserve stock, create the order, redeem the coupon.
   - In a modular monolith that is one unit of work spanning several modules' contracts, with each module writing only its own tables.
   - This is the pragmatic advantage over microservices, and it is deliberate. If a module is ever extracted, that call becomes a saga step (§9).
3. **Reference by ID, snapshot what you need.**
   - An order line stores `ProductId` plus a snapshot of name, SKU, and unit price. It never navigates to `Product`.
   - Foreign keys across modules are avoided. The one exception is `TenantId → Tenants` (shared kernel).
4. **Events for side effects, when they appear.**
   - Examples: "OrderPaid" → email, statistics, admin notification.
   - These are published through an **outbox** (Phase 14) so they are never lost and never sent for a rolled-back change.
   - In-process domain events are added only when a second consumer of the same fact exists. Until then a direct call is simpler and easier to follow.
5. **Shared kernel (minimal).** `Money`, `Currency`, `TenantId`, `Entity` base types, error types, paging primitives. Nothing business-specific.

**Today:** Ordering calls the contracts of Inventory, Shopping, Promotions, Payments and Shipping (`IInventoryReservations`, `IStockAvailability`, `IBasketCheckout`, `IPricing`, `ICouponRedemptions`, `IOrderPayments`, `IShippingRateProvider`). Catalog opens stock through its own port `IVariantStockInitializer`, which Inventory implements, so no cycle exists. `ModuleAndContractRuleTests` lists the allowed contract references and rejects cycles.

Pricing still loads `Product` through Catalog's **domain** repository rather than a snapshot contract — the planned *ISellableItems* was never built. Every crossing of that kind is listed in [ModuleDomainDependencies.md](ModuleDomainDependencies.md) and explained in [ModuleBoundaries.md](ModuleBoundaries.md).

## 7. Domain modeling strategy (DDD where it pays)

The aggregate table — each root, what it contains, its key invariants and its concurrency token — is kept in one place: [DDD.md §2](../03-DOMAIN/DDD.md#2-tactical-patterns-that-are-used). It was duplicated here and drifted; DDD.md is canonical.

**Why the Order aggregate stays small:**
- It holds its lines and its own status history. Nothing else.
- Payments, shipments, reviews, and the customer are separate aggregates referenced by ID.
- Putting "everything connected to an order" inside it would make every payment webhook and review lock the order row. That is the classic oversized-aggregate mistake.

**Value objects today:** `Money` (amount + currency, currency-aware rounding), `PostalAddress`, `CatalogText`, `OrderActor`, `CurrencyInfo`. A value object is justified when it carries rules (validation, arithmetic, normalization), not just to wrap a string — which is why slugs are normalized by the `CatalogSlug` helper and a SKU stays a string on the variant.

**Domain services:** none exist. The rule stands — a domain service only for a rule that spans aggregates *and* needs no I/O — and the one candidate did not meet it: pricing needs catalog, coupon and shipping lookups, so it is `PricingService` behind `IPricing` in the Application layer (`src/Souq.Application/Features/Baskets/Pricing/PricingService.cs`). Rules that need a database lookup belong in Application handlers and services.

**Domain events (Phase 14, [ADR-0034](../11-ADR/0034-notifications-outbox.md)):** only for facts with more than one consumer. Aggregates raise them, and they are written to the outbox in the same save as the change. Today there are two:
- `OrderStatusChanged` (paid, shipped, delivered, cancelled): feeds the customer's notification and email and the staff's new-order alert.
- `StockBecameLow`.

**Keep simple (no aggregate ceremony):** Category, Wishlist items, store configuration read models, reviews' moderation flags.

## 8. CQRS strategy

| Operation type | Approach | Why |
|---|---|---|
| **Commands** (checkout, cancel, adjust stock, create product) | MediatR command → handler → aggregate methods → unit of work | Invariants live in aggregates; the validation pipeline runs automatically |
| **Simple queries** (get order, get product) | MediatR query → **query service** in Infrastructure projecting straight into DTOs (`AsNoTracking`, `Select`) | Removes the over-fetching of loading entities and mapping in memory (Phase 0 D3); the Application layer stays EF-free |
| **Search/filter/sort/page listings** | Query service + `IPagedQuery`/`PagedQueryValidator` + `ToPageAsync` (ordered query + explicit projection), typed criteria, per-resource sort allowlists with an `Id` tiebreaker | One reusable mechanism for every listing — implemented in 1B |
| **Dashboards and reports** | Start as query services over indexed tables. Move to **read models** (pre-aggregated daily tables) when queries exceed agreed latency | The store dashboard and platform statistics are query services today (Phase 17/18); CQRS level 2 only with evidence (Phase 21) |
| **Transactional multi-step flows** (checkout) | One command orchestrating module contracts inside one transaction | Correctness first; no eventual consistency where money and stock are involved |

**MediatR:**
- **Kept**, pinned to 12.x (Apache-2.0; 13+ is commercially licensed).
- It earns its place through the **pipeline**, registered in `src/Souq.Application/DependencyInjection.cs` in this order: `UseCaseLoggingBehavior` (use-case scope and duration), `ValidationBehavior`, `AuditBehavior` (for `IAuditable` requests). Module checks are not a behaviour: `[RequiresModule]` gates the endpoint and use cases that touch a module check it ([MultiTenancy.md §4](MultiTenancy.md#4-enforcement-mechanics-phase-2-implementation)). Also through a uniform shape for every use case ([CQRS.md](CQRS.md)).
- **Not** used for module-to-module calls (those use contracts, §6) or for domain events inside an aggregate.

## 9. Future scaling and service extraction

Scale in this order, stopping as soon as the problem is solved:
1. Better queries and indexes.
2. Caching (tenant config, catalog).
3. More app instances behind a load balancer. The app is stateless: JWT, and data in the database.
4. A bigger database and read replicas for reporting.
5. Dedicated databases for enterprise tenants ([MultiTenancy.md](MultiTenancy.md)).
6. Only then, extracting a module into a service.

| Candidate | Why it might become a service | Required boundary | Contract it would expose | Data it would own | Messages | What keeps it extractable today |
|---|---|---|---|---|---|---|
| **Notifications** | Bursty I/O, provider rate limits, retries; must not slow checkout | Consumes events only; never called synchronously for business decisions | *SendNotification* (tenant, template, recipient, data) | Templates, delivery log, in-app notifications | Consumes `OrderStatusChanged`, `StockBecameLow`, `PasswordResetRequested` … through the outbox, which would publish to a broker instead | `IEmailSender` port confined to this module by a test; the outbox exists; no module reads notification tables |
| **Payments** | PCI scope isolation, provider webhooks, separate reliability needs | Owns payment state; Ordering only sees "payment succeeded/failed" | *CreatePaymentIntent*, *Refund*, webhook → *PaymentSucceeded* / *PaymentFailed* | Payments, refunds, provider configuration (encrypted secrets) | Publishes payment events; consumes *OrderPlaced* | `IPaymentService` port; webhook parsing moved behind the port (Phase 1A); no card data anywhere |
| **Search** | Relevance, facets, typo tolerance beyond SQL `LIKE` | Read-only projection of Catalog | *Search(tenant, query, filters)* | A search index (derived, rebuildable) | Consumes *ProductChanged* | Catalog listing already behind a query service; would move behind an *ICatalogSearch* port |
| **Media processing** | CPU-heavy resizing and transcoding | Receives uploads, emits variants | *ProcessMedia(upload)* → URLs | Blob storage objects | *MediaUploaded* → *MediaProcessed* | `IFileStorage` port; content validation in Application (Phase 1A) |
| **Reporting** | Heavy aggregate queries competing with OLTP | Read models fed by events or CDC | Report endpoints | Denormalized reporting store | Consumes order/payment events | Reports are query services only; no write logic depends on them |
| **Inventory** | Flash-sale contention, multi-warehouse | Owns stock and reservations; checkout calls a reservation contract | *Reserve*, *Commit*, *Release* | Inventory items, reservations, ledger | Consumes *OrderCancelled*; publishes the existing `StockBecameLow` | `IInventoryReservations` already exists as an in-process contract; reservations are explicit records, not side effects |

**The rule that keeps extraction possible:** a module that other modules call synchronously *today* (Inventory, Promotions) will need a saga when extracted, so its contract is designed as **reserve → commit/release** from the start (Phase 6/10). A module that only reacts (Notifications, Reporting, Search) can be extracted with nothing more than an outbox and a broker.

## 10. Cross-cutting building blocks

| Concern | Mechanism | Phase |
|---|---|---|
| Validation | FluentValidation + `ValidationBehavior` (async; all commands and queries); `PagedQueryValidator` for every list | ✅ 1A / 1B |
| Errors | RFC 7807 ProblemDetails, typed `Error`/`ErrorKind`, stable codes, one status table ([ADR-0017](../11-ADR/0017-error-contract.md)) | ✅ 1B |
| Reads and paging | One projection query service per module; `ToPageAsync` over an ordered query + projection | ✅ 1B |
| Concurrency | `rowversion` + `ConcurrencyConflictException` → 409 | ✅ 1A |
| Transactions | Use case owns the unit of work; no transaction spans a network call; compensation; the outbox for side effects ([ADR-0021](../11-ADR/0021-transaction-boundaries.md)) | ✅ 1B (documented) / 14 (outbox) |
| Current user and authorization | `ICurrentUser`, permission policies, ownership in use cases, explicit auth on every endpoint ([ADR-0019](../11-ADR/0019-authorization-foundation.md)) | ✅ 1B / 3 (roles) |
| Tenant context | `ITenantContext` from the host + named EF query filter (throws without a tenant) + write guard + tenant-scoped composite FKs + `tid` binding ([MultiTenancy.md §8](MultiTenancy.md#8-implementation-phase-2), [ADR-0022](../11-ADR/0022-tenancy-enforcement.md)) | ✅ 2 |
| Time | `TimeProvider`; audit timestamps in a SaveChanges interceptor | ✅ 1B |
| Logging and correlation | Request line, W3C correlation id, scopes (`CorrelationId`, `UserId`, `UseCase`; `TenantId` in 2), redaction ([ADR-0018](../11-ADR/0018-observability.md)) | ✅ 1A redaction / 1B |
| Configuration | Typed options validated at startup, fail-fast, no implicit dev fallbacks outside Development ([ADR-0020](../11-ADR/0020-configuration-and-secrets.md)) | ✅ 1B |
| Audit | `AuditEntries`, written by the `AuditBehavior` pipeline step for every `IAuditable` request, inside the handler's own transaction | ✅ 4 |
| Background work | Hosted services: `ReservationExpiryService` and `BasketCleanupService` (per store, on the `StoreSweepService` base) and `OutboxDispatcherService` | ✅ 6 / 8 / 14 |
| Architecture enforcement | `tests/Souq.ArchitectureTests` (NetArchTest + IL scan); the list of rule classes is in [DependencyRules.md](DependencyRules.md) | ✅ 1A / 1B |

**JSON conventions.** Every response is written with enums as strings (`JsonStringEnumConverter`), instants as UTC with a `Z` suffix (`UtcDateTimeJsonConverter`, `src/Souq.API/Http/UtcDateTimeJsonConverter.cs`), and `AllowInputFormatterExceptionMessages = false`, so a malformed body does not echo internal type names back to the client. All three are set once in `src/Souq.API/Program.cs`; the contract a client relies on is in [ApiDocumentation.md](../05-API/ApiDocumentation.md), which is canonical.

## 11. External integration conventions (ports and adapters)

Every integration — payments, email and storage — follows the same rules. Shipping (Phase 12) is built without an external carrier: rates come from store-defined methods behind `IShippingRateProvider`, implemented in-process, so a carrier would be the next adapter behind that contract. Notifications (Phase 14) reach providers only through the `IEmailSender` port, dispatched from the outbox.

1. **A port exists only at a real boundary:** an external system, or a technology with real variants (payment gateway, email provider, file storage, password hashing, token issuing, current user). A concrete application service with one implementation gets no interface (`OrderPaymentConfirmation`, `CustomerErasure`, `BasketResolver`). The clock is .NET's own `TimeProvider`.
2. **Ports speak our language:** `Money`, `Stream`, records. No provider SDK type appears in a port, and provider exceptions are translated at the adapter (`InvalidPaymentWebhookException`, `ConcurrencyConflictException`).
3. **Every adapter has a stand-in** for development and tests (`DemoPaymentGateway` for payments, the log email sender, the capturing test doubles). Stand-ins are selected implicitly only in Development/Testing ([ADR-0020](../11-ADR/0020-configuration-and-secrets.md)).
4. **Adapter settings are typed options validated at startup;** secrets are never logged, and provider errors are logged with masked data and truncated bodies.
5. **HTTP adapters** use `IHttpClientFactory` with a timeout. Calls happen **outside** database transactions ([ADR-0021](../11-ADR/0021-transaction-boundaries.md)).
6. **Tenant awareness enters inside adapters** (storage key prefix, per-tenant gateway keys, sender identity) without changing the ports.
