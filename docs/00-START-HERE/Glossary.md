# Glossary

> Words used in this repository, defined as **Souq uses them** — not as a textbook defines them. When a term maps to code, the code is named. Business terms come first, because they are the ones that make the code readable.
> **Related:** [SystemOverview.md](SystemOverview.md) · [EngineeringMentalModel.md](EngineeringMentalModel.md) · [DDD.md](../03-DOMAIN/DDD.md) · [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md)

## 1. The business

| Term | Meaning in Souq |
|---|---|
| **Platform** | The product itself, operated by us: one deployment serving many stores. Its control plane lives on the platform host. |
| **Platform owner** | The account that runs the platform: creates stores, assigns domains and modules, sees platform-wide statistics. Has `platform.*` permissions; never belongs to a store. |
| **Tenant / Store** | One customer's shop: its own data, domain, branding, currency, languages and enabled modules. `Tenant` is the code name; "store" is the business name. Same thing. |
| **Store admin / Staff** | Accounts *inside* a store. What they may do comes from their role's permissions (`RolePermissions`), never from a flag in the UI. |
| **Customer** | A shopper with an account in one store. Distinct from the account they sign in with: `User` is login, `Customer` is the commercial profile. |
| **Guest** | A shopper with no account. Has a basket, identified by a hashed token in an `HttpOnly` cookie, which merges into the customer's basket at sign-in. |
| **Storefront** | The public, customer-facing part of a store: catalog, basket, checkout, account. |
| **Platform area** | The admin surface served only on the platform host (`[PlatformEndpoint]`). |
| **Module (business)** | A business capability that owns rules and data: Catalog, Ordering, Payments… 13 in total ([Modules.md](../04-MODULES/Modules.md)). |
| **Module flag** | A per-store switch for an optional capability (reviews, wishlist, promotions). Disabled means the endpoint answers 404 `ModuleDisabled`, enforced by `RequiresModuleAttribute`. |
| **White-label** | One build, no brand of its own: every name, colour, font, currency and language comes from the store's configuration at runtime ([WhiteLabel.md](../08-FRONTEND/WhiteLabel.md)). |

## 2. Commerce concepts

| Term | Meaning in Souq |
|---|---|
| **Product / Variant** | A `Product` is what the customer sees; a `ProductVariant` is what is actually sold and priced (today one default variant per product). Stock hangs off the variant. |
| **Slug** | The URL-safe identifier of a product or category, unique per store. |
| **SKU** | The store's own stock-keeping code on a variant, unique per store. |
| **Basket** | The server-side list of what a shopper intends to buy. Never reserves stock and never stores prices — it re-reads them. |
| **Wishlist** | Saved products, per customer, behind the wishlist module flag. |
| **Reservation** | A hold on stock created at checkout (`StockReservation`), committed when payment succeeds, released on cancellation or expiry. Stock is `on hand − reserved = available`. |
| **Ledger / Stock movement** | The append-only record of every stock change (`StockMovement`). Every change writes exactly one entry. |
| **Coupon / Redemption** | A discount rule and one record per order that used it (`CouponRedemption`), reserved at checkout and confirmed at payment, so usage limits hold under concurrency. |
| **Shipping method** | A store-defined delivery option with its price rule, countries, estimate and carrier tracking link. |
| **Order** | The immutable commercial record of a purchase: number, snapshots of what was bought and at what price, status history with the actor of each change. |
| **Snapshot** | A copy of data taken at a moment, stored so later changes cannot rewrite history (order lines keep name, SKU and unit price; orders keep the shipping and billing address). |
| **Payment / Refund** | The money record for an order and money returned from it, both with provider references and idempotency keys. |
| **Minor units** | The smallest unit of a currency (fils, cents). Payment providers take integers in minor units; how many decimals a currency has is not universal — see open decision P-05 for JOD. |

## 3. Architecture

| Term | Meaning in Souq |
|---|---|
| **Modular monolith** | One deployable application and one database, divided inside into modules that own their data and talk through contracts. |
| **Clean Architecture** | The dependency rule `API → Infrastructure → Application → Domain`; the Domain depends on nothing ([DependencyRules.md](../02-ARCHITECTURE/DependencyRules.md)). |
| **Hexagonal / Ports and adapters** | The core defines interfaces (ports) for the outside world; Infrastructure implements them (adapters). Swapping Stripe or a mail provider touches no business code. |
| **Vertical slice** | One use case's code kept together under `Features/<Folder>`: command or query, handler, validator, DTO. |
| **Shared kernel** | The few types every module may use: `Money`, tenant identity, entity bases, `Result`/`Error`, paging. Nothing business-specific. |
| **Bounded context** | The DDD term for what Souq calls a module: a boundary inside which words have one meaning. |
| **Contract** | A module's public, in-process API (`Features/<Folder>/Contracts`). The only legal way for another module to call it. |
| **Boundary leak** | Code that reaches into another module's entities or repositories directly instead of through a contract. Some exist today and are documented per module. |

## 4. Domain modelling

| Term | Meaning in Souq |
|---|---|
| **Entity** | A thing with identity and a lifecycle (`Order`, `Product`). Its state changes through guarded methods; no public setters. |
| **Value object** | A thing defined only by its values, immutable, that carries rules: `Money` (amount + currency with currency-aware rounding), `PostalAddress`, `CatalogText`. |
| **Aggregate / Aggregate root** | A cluster of objects that changes as one unit, entered only through its root. `Order` owns its lines and status history; `Product` owns variants, images and translations. Other aggregates are referenced **by id**. |
| **Invariant** | A rule that must be true before and after every change ("available stock never goes negative", "a shipped order cannot be cancelled"). Invariants live in the aggregate. |
| **Domain service** | A rule that spans aggregates and needs no I/O, e.g. the pricing pipeline. |
| **Domain event** | A fact an aggregate raises after a state change (`OrderStatusChanged`, `StockBecameLow`), written to the outbox in the same save ([Events.md](../02-ARCHITECTURE/Events.md)). |

## 5. Application and API

| Term | Meaning in Souq |
|---|---|
| **Command** | A request that changes state (`CreateOrderCommand`). Goes through the MediatR pipeline to one handler. |
| **Query** | A request that reads (`GetInventoryQuery`). Projects straight into DTOs through a query service. |
| **Handler** | The class that executes one command or query. Orchestrates; does not hold business rules. |
| **Validator** | FluentValidation rules for a request, run by `ValidationBehavior` before the handler. |
| **Pipeline behaviour** | Cross-cutting code that wraps every request: logging, validation, auditing. |
| **Query service** | The Infrastructure implementation of a module's read port (`CatalogQueries` for `ICatalogQueries`): `AsNoTracking`, `Select`, paged. |
| **Repository** | The write-side port for an aggregate. Loads tracked entities for commands. |
| **Unit of work** | The transaction boundary a use case owns: everything in one `SaveChanges`. |
| **DTO** | A data shape for crossing a boundary. Entities are never DTOs. |
| **Projection** | Building a DTO directly in SQL instead of loading entities and mapping in memory. |
| **Result / Error code** | Expected failures travel as a `Result` with a stable `code`; the API turns them into RFC 7807 `ProblemDetails`. Clients branch on the code, never the message. |
| **Audit entry** | An append-only record of a sensitive action (all platform-area requests), written by `AuditBehavior` in the same transaction. |
| **Correlation id** | The id that ties every log line of one request together; returned in a response header. |
| **ETag** | The cache validator returned with the storefront configuration so browsers can revalidate cheaply. |

## 6. Tenancy and security

| Term | Meaning in Souq |
|---|---|
| **Tenant resolution** | Deciding which store a request belongs to, from the Host header, on the server. The client never chooses. |
| **Tenant context** | The resolved store for the current request or background scope; use cases read it, never set it. |
| **Query filter** | The EF filter applied to every tenant-owned entity so a query can only see the current store's rows. |
| **Write guard** | The `SaveChanges` check that stamps and verifies `TenantId`, so a write cannot land in another store. |
| **Composite key** | Foreign keys that carry the tenant (`TenantId`, `XId`), so the database itself refuses a cross-store reference. |
| **Permission** | A named capability (`orders.manage`) granted through roles and checked by `[HasPermission]` and inside use cases. |
| **IDOR** | Insecure direct object reference: guessing another owner's id. Souq answers **404** for another customer's or store's resource, so ids leak nothing. |
| **Platform host** | The host that serves the platform area; store endpoints answer 404 there, and vice versa. |
| **Outbox** | The table that holds messages written in a business transaction and delivered later, so nothing is lost and nothing is sent for a rolled-back change. |

## 7. Delivery and operations

| Term | Meaning in Souq |
|---|---|
| **Migration** | A code-first EF Core schema change; migrations are the only source of truth for the schema ([Migrations.md](../06-DATABASE/Migrations.md)). |
| **Seeder** | `DbSeeder`: the idempotent startup data (the demo store, the first accounts). Also the only file allowed to name the demo store. |
| **Architecture test** | A test that fails the build when a boundary is crossed (`tests/Souq.ArchitectureTests`). |
| **Testcontainers** | The library that starts a real SQL Server in Docker for the integration suite. |
| **ADR** | Architecture Decision Record: context, problem, options, decision, consequences ([docs/11-ADR/](../11-ADR/README.md)). |
| **CURRENT / PLANNED / DEFERRED / FUTURE** | Documentation labels: implemented today / scheduled in the roadmap / consciously postponed with a reason / an option nobody has scheduled. |
| **D-xx, P-xx** | Open decisions in the roadmap's decision log: `D` architectural, `P` product or commercial (for example P-05 JOD minor units, P-06 tax). |
