# How to read the code

> **What this page is:** a method for tracing any feature through the repository, from the screen a user sees to the rule that decides and the test that proves it, followed by three complete traces from different parts of Souq.
> **Level:** L1 → L2. **Read after:** [RequestLifecycle.md](RequestLifecycle.md), which walks one request in full. **Read next:** [HowToAddAFeature.md](HowToAddAFeature.md).
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`.

## The method: ten questions, always in this order

Start from what you can see (a route, a button, an error), then walk inwards. Each question has one place to look.

| # | Question | Where to look | How to find it fast |
|---|---|---|---|
| 1 | **Which route is this?** | `frontend/src/App.jsx`: `StoreRoutes` for a store host, `PlatformRoutes` for the platform host | Search the path, e.g. `path="staff"` |
| 2 | **Which page renders it, and what does it read?** | The element on that route, under `frontend/src/pages` | Pages call hooks from `frontend/src/hooks` or `frontend/src/features`, and `api.*` functions |
| 3 | **Which API call does it make?** | `frontend/src/api/client.js`: one named function per endpoint | Search the function name; it shows method and path |
| 4 | **Which endpoint answers?** | [Endpoints.md](../05-API/Endpoints.md) (generated): method, path, access, host, module flag, use case | Or search the route template in `src/Souq.API/Controllers` |
| 5 | **Who may call it?** | The attributes on the action or controller: `[HasPermission(...)]`, `[Authorize]`, `[AllowAnonymous]`, `[PlatformEndpoint]`, `[RequiresModule(...)]`, `[AvailableWhenStoreClosed]` | Which role grants a permission: `src/Souq.Application/Common/Security/Permissions.cs` |
| 6 | **Which use case runs?** | The command or query the controller sends; its handler sits beside it under `src/Souq.Application/Features/<Folder>` | [UseCases.md](../04-MODULES/UseCases.md) (generated) maps every request to its handler |
| 7 | **Which business rule decides?** | The aggregate method the handler calls, in `src/Souq.Domain` | [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) names each rule's code and test |
| 8 | **How is it stored or read?** | Writes: the repository in `src/Souq.Infrastructure/Persistence/Repositories`. Reads: the query service in `src/Souq.Infrastructure/Persistence/Queries`. Mapping: `src/Souq.Infrastructure/Persistence/Configurations` | Query services are `internal`; reach them through their port in Application |
| 9 | **Which tests prove it?** | [TestInventory.md](../10-TESTING/TestInventory.md) (generated) and [Traceability.md](../10-TESTING/Traceability.md) | Domain tests for the rule, application tests for the handler, integration tests for HTTP and isolation, Vitest for the screen, Playwright for the journey |
| 10 | **Why is it like this?** | The module's `README.md` under `docs/04-MODULES`, then its ADR from the [index](../11-ADR/README.md) | Code comments are in Arabic and usually carry the "why"; translate them rather than skip them |

**Two habits that save hours:**
- **Read the test before the code.** The test states the behaviour the code is trying to keep, including the cases that must fail.
- **Trust the generated inventories** (`Endpoints.md`, `UseCases.md`, `TestInventory.md`) over prose. They are regenerated from the compiled code, and a test fails when they drift.

---

## Trace A — Signing in to a store (authentication)

**The situation:** a store administrator enters an email and password on `/login` at their store's domain.

| # | Step | Code |
|---|---|---|
| 1 | Route | `frontend/src/App.jsx`: `/login` in both route trees, element `Login` |
| 2 | Page | `frontend/src/pages/auth/Login.jsx` calls `login(email, password)` from `frontend/src/context/AuthContext.jsx`. On success it navigates staff (`canManageStore`) to `/admin` and everyone else back to where they were. |
| 3 | API call | `frontend/src/api/client.js`: `api.login` → `publicAuth('/auth/login', …)`. Anonymous: no token, no silent refresh, so a wrong password is an answer, not an expired session. |
| 4 | Endpoint | `POST /api/auth/login` in `src/Souq.API/Controllers/AuthController.cs`, action `Login` |
| 5 | Access | `[AllowAnonymous]`; `[EnableRateLimiting(RateLimitPolicies.Auth)]` (10 attempts a minute per host and address, `src/Souq.API/Security/RateLimiting.cs`); `[AvailableWhenStoreClosed]` so a suspended store's own staff can still sign in. The controller carries `[AvailableOnAllHosts]`: the same endpoint serves store accounts on a store host and platform accounts on the platform host. |
| 6 | Use case | `LoginCommand`, `LoginValidator` and `LoginHandler` in `src/Souq.Application/Features/Auth/Commands/Login.cs` |
| 7 | Rules | `src/Souq.Domain/Identity/User.cs`: `IsLockedOut`, `RecordFailedLogin` (locked after `MaxFailedLogins` = 5). The handler verifies the password through `IPasswordHasher` and runs a fake verification for an unknown email, so timing doesn't reveal which emails exist. A disabled account is only revealed after a correct password. |
| 8 | Persistence and tokens | The user is loaded through `IUserRepository`, filtered to the host's scope: this store's accounts, or platform accounts. `AuthSessionIssuer` (`src/Souq.Application/Features/Auth/AuthSessionIssuer.cs`) creates a `RefreshToken` in a new family and a short-lived JWT from `JwtTokenGenerator` (`src/Souq.Infrastructure/Services/JwtTokenGenerator.cs`), carrying the store id and a security stamp. Back in the controller, `Session` writes the raw refresh token into the `souq_refresh` cookie (`HttpOnly`, path `/api/auth`) and returns the access token and user in the body. |
| 9 | Tests | `tests/Souq.Domain.Tests/UserTests.cs` (lockout) · `tests/Souq.Application.Tests/Auth/AuthHandlersTests.cs` · `tests/Souq.IntegrationTests/AuthSessionTests.cs` (rotation, reuse detection, host binding) |
| 10 | Why | [ADR-0010](../11-ADR/0010-authentication-authorization.md), [ADR-0023](../11-ADR/0023-sessions-and-credentials.md), [Identity module](../04-MODULES/Identity/README.md) |

**What happens on the next request:** the browser keeps the access token only in memory. Every protected call goes through `AccessTokenValidation` (`src/Souq.API/Security/AccessTokenValidation.cs`), which refuses a token whose store isn't the host's store, or whose security stamp changed because the password changed or the account was disabled. On `401` the client calls `POST /api/auth/refresh` once, and the server rotates the refresh token. Presenting an old one is treated as theft, and the whole family is revoked.

**What not to do:** store the access token in `localStorage`, or make the frontend decide permissions. `can()` in `AuthContext.jsx` only shapes the menu; the server checks again on every call.

---

## Trace B — The storefront catalogue (a read: the query side)

**The situation:** a visitor opens a store's home page and pages through its products.

| # | Step | Code |
|---|---|---|
| 1 | Route | `frontend/src/App.jsx`: the index route of `StoreRoutes`, element `Store` |
| 2 | Page | `frontend/src/pages/Store.jsx` renders `frontend/src/pages/Storefront.jsx`, which renders `frontend/src/components/catalog/Catalog.jsx`. That reads `useCatalog` (`frontend/src/hooks/useCatalog.js`): TanStack Query with the key `queryKeys.products(params)` and `keepPreviousData`, so paging never flashes "no results". Filters live in the URL. |
| 3 | API call | `api.getProducts` in `frontend/src/api/client.js` → `GET /api/products?…` |
| 4 | Endpoint | `ProductsController.GetAll` in `src/Souq.API/Controllers/ProductsController.cs` |
| 5 | Access | `[AllowAnonymous]`: the catalogue is public. **Which** catalogue is still the host's store, resolved before the controller runs. A closed store answers `503` here, because only endpoints marked `[AvailableWhenStoreClosed]` stay open. |
| 6 | Use case | `GetProductsQuery` and `GetProductsQueryValidator` (page ≥ 1, page size capped), handled by `GetProductsHandler` (`src/Souq.Application/Features/Products/Queries/GetProductsHandler.cs`). The handler knows no SQL. It turns the request into a `ProductSearch` and calls the port `ICatalogQueries`, passing the store's default language from `ITenantContext`. |
| 7 | Rules | Reads don't go through aggregates. The visibility rule — only **published** products in an **active** category — is one private method, `VisibleProducts`, in the query service. Every storefront read uses it, so product detail, search and "related products" can't disagree. |
| 8 | Persistence | `CatalogQueries` (`src/Souq.Infrastructure/Persistence/Queries/CatalogQueries.cs`, `internal`) projects straight into `ProductDto` with `AsNoTracking`, deterministic ordering and server paging (`PaginatedList`). The tenant query filter in `AppDbContext` adds the store predicate automatically; the query never mentions a store id. |
| 9 | Tests | `tests/Souq.IntegrationTests/CatalogTests.cs`: the admin sees every status while the store sees only published products; a hidden category hides its products; catalogue lists run a fixed number of SQL queries, not one per product. `tests/Souq.IntegrationTests/TenantIsolationTests.cs`: store lists show no rows from another store. `frontend/src/pages/storefrontPages.test.jsx` covers the pages. |
| 10 | Why | [ADR-0008](../11-ADR/0008-cqrs-strategy.md) (reads through projection query services), [ADR-0025](../11-ADR/0025-catalog-model.md), [Catalog module](../04-MODULES/Catalog/README.md) |

**What this trace teaches:** CQRS here isn't two databases. It is two paths through one: commands load aggregates and save them; queries project rows into DTOs. That is why a read never needs a repository or a domain method.

**What not to do:** return `IQueryable` from the query service so a handler can "just add a filter". It would compose SQL outside the reviewed place, where paging and visibility can be forgotten. An architecture test forbids it.

---

## Trace C — The platform owner suspends a store (platform and tenancy)

**The situation:** on the platform host, the platform owner suspends a client's store. Within a minute its visitors see a branded "closed" screen, and its staff can still sign in.

| # | Step | Code |
|---|---|---|
| 1 | Route | `frontend/src/App.jsx`: `PlatformRoutes` → `stores/:id`, guarded by `platformGuarded(…, 'platform.tenants.manage')` |
| 2 | Page | `frontend/src/pages/platform/StoreDetail.jsx` renders `LifecyclePanel` from `frontend/src/pages/platform/StorePanels.jsx`. The actions offered come from the store's status. **Suspend** opens `ConfirmDialog` (`frontend/src/components/common/ConfirmDialog.jsx`); archiving additionally requires typing the store's slug. |
| 3 | API call | `api.changePlatformStoreStatus(id, 'Suspend')` → `POST /api/platform/tenants/{id}/status` with `{ "action": "Suspend" }` |
| 4 | Endpoint | `PlatformTenantsController.ChangeStatus` in `src/Souq.API/Controllers/PlatformControllers.cs` |
| 5 | Access | `[PlatformEndpoint]`: this endpoint answers `404` on any store host. `[HasPermission(Permissions.Platform.Tenants)]`. A store's token is refused on the platform host, because `AccessTokenValidation` requires a platform token there. This is the one area where a store id comes from the route, which is why every request type under `Features/Platform` must be audited (an architecture test). |
| 6 | Use case | `ChangeTenantStatusCommand` and `ChangeTenantStatusHandler` in `src/Souq.Application/Features/Platform/TenantAdministration.cs`. The command implements `IAuditable` (action `tenant.suspended`), so `AuditBehavior` writes the audit line in the same transaction as the change. |
| 7 | Rules | `src/Souq.Domain/Platform/Tenant.cs`: `Suspend` is allowed only from `Active`; anything else throws `InvalidTenantOperationException` and answers `422`. After saving, the handler calls `ITenantDirectory.Invalidate()`, so the cached host → store map (`TenantDirectoryCache`) stops serving the old status. |
| 8 | Effect on the store host | The next request to the store's host resolves the store with status `Suspended`. `TenantAvailabilityMiddleware` (`src/Souq.API/Tenancy/TenantAvailability.cs`) answers `503 StoreUnavailable` to everything except endpoints marked `[AvailableWhenStoreClosed]`: the storefront configuration and sign-in. In the browser, `TenantProvider` maps that answer through `bootOutcome` (`frontend/src/app/tenantModel.js`) to `closed`, and `frontend/src/app/BootScreens.jsx` draws the store's own closed screen. |
| 9 | Tests | `tests/Souq.Domain.Tests/TenantTests.cs` (allowed transitions) · `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs` (provisioning to suspension, then `503 StoreUnavailable`) · `tests/Souq.IntegrationTests/ProvisioningBoundaryTests.cs` (the owner's token is refused on the store) · `frontend/src/pages/platform/Provisioning.test.jsx` · `frontend/e2e/platform-provisioning.spec.js` (the full lifecycle in a real browser, on two hosts) |
| 10 | Why | [ADR-0006](../11-ADR/0006-tenant-resolution.md), [ADR-0022](../11-ADR/0022-tenancy-enforcement.md), [ADR-0024](../11-ADR/0024-platform-administration.md), [Platform module](../04-MODULES/Platform/README.md) |

**What this trace teaches:** one lifecycle change crosses two hosts and two frontends. The server stays consistent through one decision point (`TenantAvailabilityMiddleware`), not through checks scattered across controllers.

**What not to do:** give the platform owner a way into a store's admin "to help". The platform can provision a store and invite its administrator, but it cannot act as a store user. An audited support mode is written down as a future option, not a shortcut ([MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md)).

---

## When the trail goes cold

| Symptom | Where the answer usually is |
|---|---|
| The endpoint exists but answers `404` | Wrong host (platform vs store), a module the store disabled (`[RequiresModule]`), or another store's id: all deliberate. See [Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md). |
| You can't find where a value is computed | Search the Domain first (`src/Souq.Domain`), then the pricing pipeline (`src/Souq.Application/Features/Baskets/Pricing`) |
| A handler seems to do nothing after saving | A domain event was raised: search `Raise(` in the aggregate, then the event's handler under `src/Souq.Application/Features/Notifications` |
| A rule "should" exist but you can't find it | Check [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md). If it isn't there, it probably doesn't exist, and adding it may be a business decision (`AGENTS.md` §9). |
| Behaviour differs between two screens | Two readers of the same data: compare their query services, and check whether one reads a projection the other doesn't |
