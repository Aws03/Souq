# Repository map: what lives where, and why

> **What this page is:** the shape of the repository, folder by folder, with the rule for what belongs in each and what must never be added there. It answers "where do I put this?" and "why does this file exist?" without opening 400 files.
> **Related:** [HowToReadThisRepository.md](HowToReadThisRepository.md) (reading paths) · [DependencyRules.md](../02-ARCHITECTURE/DependencyRules.md) (what may reference what) · [Modules.md](../04-MODULES/Modules.md) (business modules)

## Top level

| Path | What it is |
|---|---|
| `src/` | The backend: four layer projects (below) |
| `frontend/` | The React single-page app that renders any store |
| `tests/` | Four .NET test projects; frontend tests live beside the code they test |
| `docs/` | This knowledge system, numbered by purpose |
| `scripts/` | Operational scripts (backup, restore, restore drill) — `bash` + `sqlcmd`, no build step |
| `.github/` | The CI pipeline — build, the five suites, dependency audit and secret scan |
| `docker-compose.yml` | The three-container stack: database, API, web |
| `.env.example` | Every environment variable the stack reads, documented. The real `.env` is the owner's and is never read or committed |
| `AGENTS.md` | The engineering contract for humans and AI agents. Read before changing anything |
| `CLAUDE.md` | Claude Code-specific preferences; defers to `AGENTS.md` |
| `Souq.sln` | The solution: the four source projects and the four test projects |

## `src/Souq.Domain` — the business model

Depends on nothing. No EF Core, no ASP.NET, no MediatR, no provider SDKs. If a rule is true regardless of storage, transport or UI, it belongs here.

| Path | Holds | Do not add |
|---|---|---|
| `src/Souq.Domain/Entities` | Most aggregates and entities: orders, products, inventory, customers, coupons, reviews, payments, baskets, wishlist, notifications, shipping methods | Framework attributes, DTOs, anything needing I/O |
| `src/Souq.Domain/Identity` | `User`, `RefreshToken` — accounts and sessions. Separate namespace because these rows may belong to a store **or** to the platform | Commerce profile data (that is `Customer`) |
| `src/Souq.Domain/Platform` | `Tenant`, its domains, `StoreSettings`, `StoreModules`, brand presets: the store itself as data | Anything tenant-owned (those rows carry a `TenantId` instead) |
| `src/Souq.Domain/ValueObjects` | `Money`, `PostalAddress`, `CatalogText`, `CurrencyInfo`: immutable values that carry rules | Wrappers that only rename a string |
| `src/Souq.Domain/Enums` | Business vocabularies: statuses, kinds, reasons | Enums that exist only for an API payload |
| `src/Souq.Domain/Events` | `IDomainEvent` and the events aggregates raise | Event handlers (they live in Application) |
| `src/Souq.Domain/Exceptions` | Domain exceptions, each with a stable code | Exceptions for technical failures |
| `src/Souq.Domain/Interfaces` | Repository ports, one per aggregate, plus `IUnitOfWork` | Query/reporting interfaces (those are Application ports) |
| `src/Souq.Domain/Common` | `Entity`, `BaseEntity`, the tenant-ownership marker interfaces, `Roles` | Business logic for a specific module |
| `src/Souq.Domain/Auditing` | `AuditEntry`: the append-only record of sensitive actions | The decision of *what* to audit (that is a behaviour in Application) |

## `src/Souq.Application` — use cases

Depends on Domain, MediatR and FluentValidation. Knows *what* happens, never *how* a technology does it.

| Path | Holds | Do not add |
|---|---|---|
| `src/Souq.Application/Features/<Folder>` | One folder per feature area, mapped to a business module: commands, queries, handlers, validators, DTOs | EF Core, HTTP types, provider SDKs |
| `src/Souq.Application/Features/<Folder>/Contracts` | The module's **public** in-process API for other modules | Types only this module uses |
| `src/Souq.Application/Common/Interfaces` | Ports to the outside world: payments, storage, tokens, hashing | A port with a single implementation and no real variation point |
| `src/Souq.Application/Common/Notifications` | The outbox contracts, message types and the email abstraction | Provider code |
| `src/Souq.Application/Common/Security` | `Permissions`, `RolePermissions`, `ICurrentUser` | Authentication mechanics (JWT lives in Infrastructure) |
| `src/Souq.Application/Common/Tenancy` | `ITenantContext` and the tenant scope abstractions | Host parsing (that is the API's middleware) |
| `src/Souq.Application/Common/Behaviors` | The MediatR pipeline: logging, validation, auditing | Business rules |
| `src/Souq.Application/Common/Models` | `Result`, `Error`, paging primitives | Module-specific DTOs |
| `src/Souq.Application/Common/Auditing` | `IAuditable` and how an audited request describes itself | The audit table (Domain) or its writer (Infrastructure) |
| `src/Souq.Application/Common/Accounts` | Account building blocks shared by store staff and platform users: invitations, status changes | Module-specific identity rules |
| `src/Souq.Application/Common/Files` | Upload inspection and media rules | File system or blob code |
| `src/Souq.Application/DependencyInjection.cs` | What the Application layer registers, including the pipeline order and the outbox handlers | Infrastructure registrations |

## `src/Souq.Infrastructure` — adapters

Implements the ports. The only layer allowed to know EF Core, SQL Server, Stripe, SMTP, the file system.

| Path | Holds | Do not add |
|---|---|---|
| `src/Souq.Infrastructure/Persistence` | `AppDbContext` (the tenant query filter and the write guard), `DbSeeder`, migrations | Business decisions |
| `src/Souq.Infrastructure/Persistence/Configurations` | One EF configuration per entity: tables, keys, indexes, precision | Data annotations on Domain entities |
| `src/Souq.Infrastructure/Persistence/Repositories` | Write-side repositories | Read projections |
| `src/Souq.Infrastructure/Persistence/Queries` | Read-side query services (internal; reached through their Application ports) | Writes |
| `src/Souq.Infrastructure/Persistence/Interceptors` | Audit timestamps and the tenant write guard | Module logic |
| `src/Souq.Infrastructure/Persistence/Outbox` | The outbox table, its writer and its processor | Handlers (they live in Application) |
| `src/Souq.Infrastructure/Payments` | The gateway router, the Stripe adapter and the fake gateway | Order rules |
| `src/Souq.Infrastructure/Notifications` | Email composition and the store's link origins | Message types |
| `src/Souq.Infrastructure/Security` | Hashing, token issuing, secret protection | Authorization decisions |
| `src/Souq.Infrastructure/Tenancy` | The tenant directory and cache, store configuration, tenant scopes for background work | Host parsing (API) |
| `src/Souq.Infrastructure/BackgroundJobs` | Hosted services: outbox dispatch, reservation expiry, basket cleanup | Use-case logic (call a use case instead) |
| `src/Souq.Infrastructure/Services` | Remaining adapters: file storage, email providers, provider selection, amount conversion | Anything a module owns |

## `src/Souq.API` — the delivery layer

| Path | Holds | Do not add |
|---|---|---|
| `src/Souq.API/Controllers` | Thin controllers: bind, authorize, send one request, map the result | Business rules, database access, claims parsing |
| `src/Souq.API/Tenancy` | Host → store resolution, availability and module gating, tenancy options | Data access |
| `src/Souq.API/Security` | Permission policies, the current-user adapter, rate limiting, token validation | Permission *definitions* (Application) |
| `src/Souq.API/Http` | The error contract, result → HTTP mapping, correlation, request helpers | Feature logic |
| `src/Souq.API/Middleware` | The global exception handler | Anything a filter or policy can do |
| `src/Souq.API/Observability` | Request logging and the correlation header | Business metrics |
| `src/Souq.API/Program.cs` | The composition root: registrations, the middleware order, auth, rate limits, startup | Anything that can live in a layer |
| `src/Souq.API/appsettings.json` | Non-secret defaults | Secrets of any kind |

## `frontend/src` — the single-page app

One build renders any store; the server decides, the UI displays ([FrontendGuide.md](../08-FRONTEND/FrontendGuide.md)).

| Path | Holds | Do not add |
|---|---|---|
| `frontend/src/app` | Boot: the store configuration, the theme, boot screens, the platform shell | Feature logic |
| `frontend/src/api` | The HTTP client: base URL, credentials, silent refresh, error normalization | Business rules |
| `frontend/src/pages` | Screens, grouped by area (storefront, `account`, `admin`, `auth`, `checkout`) | Reusable logic (extract to `features/`) |
| `frontend/src/features` | Pure, testable logic per feature: payload builders, view models, formatting | React components |
| `frontend/src/components` | The UI kit and shared components | Anything that calls the API directly |
| `frontend/src/context` | Cross-cutting client state: auth, cart, wishlist, toasts | Server state caches |
| `frontend/src/i18n` | Translations and locale helpers | Hard-coded user-visible strings anywhere else |
| `frontend/src/styles.css` | Semantic design tokens and the reset | Brand colours or fonts (they come from the store) |

## `tests`

| Path | Proves |
|---|---|
| `tests/Souq.Domain.Tests` | Business rules and invariants, with no infrastructure |
| `tests/Souq.Application.Tests` | Use-case orchestration, permissions and ownership, with test doubles |
| `tests/Souq.ArchitectureTests` | The boundaries themselves: layers, modules, tenancy, endpoints, documentation, and the generated inventories |
| `tests/Souq.IntegrationTests` | The real API over SQL Server in Docker: HTTP contract, tenant isolation, concurrency, migrations |
| `frontend/src/**/*.test.js` | Pure frontend logic (Vitest) |

Details and what belongs where: [TestingStrategy.md](../10-TESTING/TestingStrategy.md).

## Files worth knowing by name

| File | Why it exists |
|---|---|
| `src/Souq.API/Program.cs` | The whole runtime in one file: what is registered, in which order middleware runs, and which checks refuse to start |
| `src/Souq.Infrastructure/Persistence/AppDbContext.cs` | Where tenant isolation is actually enforced: the query filter, the write guard, and domain events captured into the outbox |
| `src/Souq.API/Tenancy/TenantResolutionMiddleware.cs` | Turns a host name into the current store; the only place that decision is made |
| `src/Souq.API/Tenancy/TenantAvailability.cs` | Decides 404 (wrong host or disabled module) and 503 (store closed) before any handler runs |
| `src/Souq.API/Security/PermissionAuthorization.cs` | Turns `[HasPermission("orders.manage")]` into a policy, and a misspelled permission into a loud failure |
| `src/Souq.Application/Common/Security/Permissions.cs` | The permission catalog: the vocabulary the whole system authorizes against |
| `src/Souq.Application/Common/Models/Result.cs` | How expected failures travel without exceptions |
| `src/Souq.API/Middleware/GlobalExceptionHandler.cs` | The single place that turns any failure into the RFC 7807 contract |
| `src/Souq.Application/Common/Notifications/Outbox.cs` | The outbox contracts, message allow-list and retry policy |
| `src/Souq.Infrastructure/Persistence/DbSeeder.cs` | What exists in a fresh database, and the only file allowed to name the demo store |
| `src/Souq.Domain/ValueObjects/Money.cs` | Money with its currency and rounding rules — the reason totals are consistent everywhere |
| `src/Souq.Domain/Entities/OrderTransitions.cs` | The order state machine as data: which status may follow which |
| `tests/Souq.ArchitectureTests/ModuleMap.cs` | The single source for "which feature folder belongs to which module" |
| `tests/Souq.IntegrationTests/Infrastructure/SouqApiFactory.cs` | The real API on a throwaway SQL Server; read it before writing an integration test |
| `frontend/src/app/TenantProvider.jsx` | The frontend's first request: the store's identity, languages, currency and modules |
| `frontend/src/api/client.js` | Every call the frontend makes, plus session handling and error translation |
| `frontend/src/components/ProtectedRoute.jsx` | The UX guards: authentication, permission and module gates (never the enforcement) |
