# Reporting module

> **Code:** `src/Souq.Application/Features/Reporting`, `src/Souq.Infrastructure/Persistence/Queries/PlatformQueries.cs` · **Decisions:** [ADR-0008](../../11-ADR/0008-cqrs-strategy.md), [ADR-0024](../../11-ADR/0024-platform-administration.md) · **Change guide:** none yet — the module is one query

## Purpose

Reporting answers questions that span more than one module, and sometimes more than one store: how many stores are active, how many orders were placed in the last 30 days. It is its own module because those answers are **derived** data with no owner: they read other modules' tables, they never write, and they are allowed to cross the tenant boundary in a way no business module may. Keeping that privilege in one named module makes it reviewable.

Today the module is small and honest about it: one query, the platform statistics. Store-facing dashboards are **PLANNED** (roadmap Phase 17).

## Responsibilities

- Cross-store counts for the platform owner and platform administrators.
- Being the only module allowed to answer "how many, across everything", and doing it through the single reviewed query-filter bypass.

## Not this module's job

| Not this module | Owner |
|---|---|
| Any write to business data | The owning module. Reporting is read-only by definition |
| The store list, a store's detail, the audit log | [Platform](../Platform/README.md) — those are administration, not reporting, even though they sit behind neighbouring endpoints |
| Per-store operational lists (low stock, pending reviews, the order queue) | The owning module's own query service, filtered by the tenant filter like any other read |
| Deciding who may read a report | [Identity](../Identity/README.md) — Reporting declares the permission, the policy enforces it |

## Business concepts

- **Platform statistics** — a snapshot of the whole platform: stores by status, account counts, and the totals of customers, products and orders.
- **Recent window** — "the last 30 days", computed by the handler from the injected clock, not by the database.
- **Store dashboard** — the KPIs a store's own staff need. PLANNED; see Future evolution.

## Domain model

This module owns no entities, no value objects and no domain events. Its only types are the read contract and its DTO:

| Type | Kind | Path | Notes |
|---|---|---|---|
| `PlatformStatsDto` | read model | `src/Souq.Application/Features/Reporting/PlatformStats.cs` | `TenantsByStatus` (every `TenantStatus` name, zero-filled), `PlatformAccounts`, `StoreStaffAccounts`, `Customers`, `Products`, `Orders`, `OrdersLast30Days` |
| `IPlatformReports` | port | same file | Implemented by `PlatformQueries` in Infrastructure |
| `GetPlatformStatsQuery` | query, `IAuditable` | same file | Audit action `platform.stats.viewed` |

## Use cases

| Use case | Query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| Platform statistics | `GetPlatformStatsQuery` | `GetPlatformStatsHandler` | `platform.reports.view` (PlatformOwner, PlatformAdmin), platform hosts only | `GET /api/platform/stats` |

The handler does one thing: it turns "now" into the recent-window boundary (`clock` minus 30 days) and delegates to `IPlatformReports`. All counting is SQL.

## Public contracts

- **Provides:** `IPlatformReports` — implemented in Infrastructure, consumed only by this module's handler.
- **Consumes:** nothing from other modules' Application layers. It reads their *tables* through Infrastructure, which is the documented exception in [Modules.md](../Modules.md).

## Dependencies

- **Uses:** `PlatformQueries`, which is also `IPlatformQueries` for the platform area. It counts `Tenants` (no filter — a platform table), and `Users`, `Customers`, `Products` and `Orders` with `IgnoreQueryFilters` on the named `Tenant` filter. Every one of those is an aggregate count: no row of any store ever leaves the query.
- **Used by:** `PlatformInsightsController` in `src/Souq.API/Controllers/PlatformControllers.cs`.
- **Enforced vs convention:**
  - Enforced — `TenancyRuleTests` allows `IgnoreQueryFilters` in exactly one type (`PlatformQueries`); adding a second is a security decision that fails the build first.
  - Enforced — `ModuleAndContractRuleTests` requires every request under `Features.Reporting` to implement `IAuditable`, exactly as for the platform area: a cross-store read is an event worth recording.
  - Convention — "Reporting never writes" is not enforced by a test. The port returns a DTO and the implementation only counts, but nothing would stop a future method from mutating.

## Data ownership

The module owns **no tables**. It reads `Tenants`, `Users`, `Customers`, `Products` and `Orders`. There are no read models, no materialized views and no caches: each request runs seven aggregate queries against the live tables.

Counting semantics worth knowing before quoting a number:

- `TenantsByStatus` counts every store, including archived ones.
- `PlatformAccounts` counts users with no store, and `StoreStaffAccounts` counts `TenantAdmin` and `TenantStaff` across all stores — both include disabled accounts and unaccepted invitations.
- `Customers`, `Products` and `Orders` are raw row counts: erased customers, archived products and cancelled orders are all included.
- `OrdersLast30Days` counts by `CreatedAt`, in UTC.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/platform/stats` | `platform.reports.view`, platform host only (`[PlatformEndpoint]`) | — | Platform statistics |

## Security and permissions

- `platform.reports.view` is granted to PlatformOwner and PlatformAdmin.
- `store.reports.view` exists in `Permissions` and is granted to TenantAdmin and TenantStaff, but **no endpoint uses it today** — it was defined ahead of the Phase 17 dashboard so that roles would not have to change shape later.
- The endpoint is served only on platform hosts, and a store token is rejected there with 401 (`AuthorizationBoundaryTests`).
- The query is audited like every platform-area request: the row is staged before the handler and flushed after success, because a query saves nothing of its own.

## Tenant behaviour

This is the one module that deliberately reads across tenants. Two rules keep it safe:

1. Only aggregates leave the bypass — a count, never a row. There is no code path by which one store's data reaches another store's screen.
2. It is reachable only on a platform host, behind a platform permission, and every call is recorded in `AuditEntries`.

## Events and background work

None. No outbox messages, no hosted services, no scheduled aggregation.

## External integrations

None.

## Tests

| Level | Class | What it covers |
|---|---|---|
| Integration | `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs` | That a PlatformAdmin can call `GET /api/platform/stats` (status only) |
| Integration | `tests/Souq.IntegrationTests/AuthorizationBoundaryTests.cs` | 404 on a store host, 401 for anonymous callers and for store tokens, and that the endpoint declares a permission |
| Architecture | `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` | The bypass allowlist |
| Architecture | `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs` | Every Reporting request is auditable; the module's folder map |

**Gap:** no test asserts the numbers. A change to `PlatformQueries.GetStatsAsync` that quietly counted the wrong thing — archived stores, or orders in every status — would be caught by nothing.

## Failure modes

| Situation | Error code | HTTP | How it is handled |
|---|---|---|---|
| Anonymous caller on a platform host | `Unauthenticated` | 401 | Permission policy |
| Store token on a platform host | `Unauthenticated` | 401 | `AccessTokenValidation` (a platform host must see no `tid`) |
| Platform account without `platform.reports.view` | `Forbidden` | 403 | `PermissionAuthorizationHandler` |
| Called on a store host | `NotFound` | 404 | `TenantAvailabilityMiddleware` (`[PlatformEndpoint]`) |
| A database error while counting | `ServerError` | 500 | `GlobalExceptionHandler`; the staged audit row is discarded |

There are no business failures: the query cannot fail a rule, only a database call.

## Common change scenarios

- **Add a number to the platform statistics.** Extend `PlatformStatsDto` and `PlatformQueries.GetStatsAsync`, keeping every addition an aggregate. Do not introduce a second `IgnoreQueryFilters` type — `TenancyRuleTests` will fail, and it is meant to.
- **Add a store-scoped report.** It does **not** need the bypass: inside a store scope the tenant filter already narrows every table. Put the query behind a new port in `Features/Reporting`, implement it in a query service in `src/Souq.Infrastructure/Persistence/Queries`, and protect the endpoint with `store.reports.view`. Remember that `ModuleAndContractRuleTests` will require the request to implement `IAuditable` because it lives under `Features.Reporting` — decide whether auditing a store's own dashboard is what you want, or whether the query belongs in the owning module instead.
- **Make statistics cheaper.** Seven queries per request is fine for the current scale and terrible at ten thousand stores. The options, in order of cost: cache the DTO for a minute; precompute nightly into a table; feed read models from events. The first is reversible in an afternoon, the last is a new ADR.

## Known limitations

- One endpoint. No revenue, no growth over time, no per-store breakdown, no time series — `OrdersLast30Days` is the only temporal figure, and there is nothing to compare it to.
- Every call recounts the whole platform: seven aggregate queries over unindexed-for-this-purpose tables, with no cache.
- Counts include archived, cancelled, disabled and erased rows, which is rarely what a dashboard wants to show.
- No store-facing reporting endpoint exists, although the permission does. The admin dashboard in `frontend/src/pages/admin/Dashboard.jsx` fills the gap by calling ordinary endpoints and reading their `totalCount` — `api.getProducts({ pageSize: 1 })`, `api.getCategories()` and `api.getLowStock({ pageSize: 1 })`. It is a page counting pages, not a report.
- The module has no Domain and no tests of its own numbers, so its correctness rests entirely on reading `PlatformQueries`.

## Future evolution

- **PLANNED (Phase 17):** the tenant admin dashboard — orders, revenue, average order value, low stock and recent activity for one store. That is where `store.reports.view` and this module's first store-scoped queries belong.
- **PLANNED (Phase 18):** platform statistics and store health in the platform dashboard, which is the first real consumer of `GET /api/platform/stats`.
- **PLANNED (Phase 21):** the performance review, which is the natural moment to decide between caching and precomputation.
- **FUTURE:** event-fed read models, and a separate reporting store — [Modules.md](../Modules.md) names Reporting as an extraction candidate precisely because it only reads.
