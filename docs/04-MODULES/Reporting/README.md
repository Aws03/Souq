# Reporting module

> **Code:** `src/Souq.Application/Features/Reporting`, `src/Souq.Infrastructure/Persistence/Queries/PlatformQueries.cs`, `src/Souq.Infrastructure/Persistence/Queries/StoreReportQueries.cs` · **Decisions:** [ADR-0008](../../11-ADR/0008-cqrs-strategy.md), [ADR-0024](../../11-ADR/0024-platform-administration.md) · **Change guide:** none yet · **The dashboards, their metric definitions and their limits:** [Dashboards.md](Dashboards.md)

## Purpose

Reporting answers questions that span more than one module, and sometimes more than one store: how many stores are active, how many orders were placed in the last 30 days. It is its own module because those answers are **derived** data with no owner: they read other modules' tables, they never write, and they are allowed to cross the tenant boundary in a way no business module may. Keeping that privilege in one named module makes it reviewable.

The module has two read paths. The **platform statistics** cross every store and use the single reviewed query-filter bypass. The **store dashboard** does not cross anything: inside a store scope the ordinary tenant filter already narrows every table, so it is a normal query service that happens to live here because a dashboard is derived data with no owning module. Every metric it reports is defined in [Dashboards.md](Dashboards.md) §2.

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
- **Store dashboard** — one store's own figures for a chosen period: net revenue, counted orders, average order value, new customers, a daily trend, orders by status, best sellers, category performance, a stock snapshot and customer counts. Read by two different screens for two different readers ([Dashboards.md](Dashboards.md) §1).
- **A counted order** — placed **and** `Paid`, `Shipped` or `Delivered`. This one definition is the basis of every money figure, and it lives in `StoreReportQueries.CountedStatuses` so that a card and a chart on the same screen cannot disagree about what revenue is.
- **Report range** — a closed key (`Today`, `Last7Days`, `Last30Days`, `Last90Days`, `ThisYear`), not two dates from the browser. The handler turns the key into a window from the injected clock.

## Domain model

This module owns no entities, no value objects and no domain events. Its only types are the read contract and its DTO:

| Type | Kind | Path | Notes |
|---|---|---|---|
| `PlatformStatsDto` | read model | `src/Souq.Application/Features/Reporting/PlatformStats.cs` | `TenantsByStatus` (every `TenantStatus` name, zero-filled), `PlatformAccounts`, `StoreStaffAccounts`, `Customers`, `Products`, `Orders`, `OrdersLast30Days` |
| `IPlatformReports` | port | same file | Implemented by `PlatformQueries` in Infrastructure |
| `GetPlatformStatsQuery` | query, `IAuditable` | same file | Audit action `platform.stats.viewed` |
| `StoreDashboardDto` | read model | `src/Souq.Application/Features/Reporting/StoreDashboard.cs` | `PeriodTotalsDto` for the period and the one before it, `SalesPointDto` trend, `OrdersByStatus` (zero-filled), `TopProductDto` and `CategoryPerformanceDto` lists, `InventorySnapshotDto`, customer counts, the currency |
| `ReportRange` | enum | same file | The closed period key. An unknown value is a 400, not a silent default |
| `ReportWindow` | value | same file | `From`, `To` and `PreviousFrom`; computed by `GetStoreDashboardHandler.WindowFor` from the injected clock |
| `IStoreReports` | port | same file | Implemented by `StoreReportQueries` in Infrastructure |
| `GetStoreDashboardQuery` | query, `IAuditable` | same file | Audit action `store.dashboard.viewed` |

The header comment of `StoreDashboard.cs` carries the formula for every metric, including the two that are deliberately **absent** — profit and conversion rate — and the reason each cannot be computed from the data this system holds.

## Use cases

| Use case | Query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| Platform statistics | `GetPlatformStatsQuery` | `GetPlatformStatsHandler` | `platform.reports.view` (PlatformOwner, PlatformAdmin), platform hosts only | `GET /api/platform/stats` |
| Store dashboard | `GetStoreDashboardQuery` | `GetStoreDashboardHandler` | `store.reports.view` (TenantAdmin, TenantStaff), on the store's own host | `GET /api/admin/reports/dashboard?range=` |

Both handlers do one thing: they turn "now" into a window and delegate. All aggregation is SQL — nothing loads rows and sums them in memory, so a store with a hundred thousand orders costs the dashboard what a store with a hundred costs.

**Frontend.** The store dashboard is read by `/admin` (`frontend/src/pages/admin/Dashboard.jsx`) and `/admin/business` (`frontend/src/pages/admin/BusinessOverview.jsx`) through `api.getStoreDashboard`; the view logic is in `frontend/src/features/reporting/dashboardView.js`, `frontend/src/features/reporting/businessHealth.js` and `frontend/src/features/reporting/chartScales.js`. The platform statistics are read by `/platform` (`frontend/src/pages/platform/PlatformOverview.jsx`) and the platform layout (`frontend/src/app/PlatformLayout.jsx`) through `api.getPlatformStats`.

## Public contracts

- **Provides:** `IPlatformReports` and `IStoreReports` — both implemented in Infrastructure, each consumed only by its own handler.
- **Consumes:** nothing from other modules' Application layers. It reads their *tables* through Infrastructure, which is the read-side exception documented in [ModuleBoundaries.md §5](../../02-ARCHITECTURE/ModuleBoundaries.md#5-what-is-not-enforced--and-the-ratchet-that-keeps-it-honest).

## Dependencies

- **Uses:** `PlatformQueries`, which is also `IPlatformQueries` for the platform area. It counts `Tenants` (no filter — a platform table), and `Users`, `Customers`, `Products` and `Orders` with `IgnoreQueryFilters` on the named `Tenant` filter. Every one of those is an aggregate count: no row of any store ever leaves the query.
- **Uses:** `StoreReportQueries` for the store dashboard. It uses **no** bypass: every statement passes the ordinary tenant filter, so a mistake in this file cannot leak another store's rows.
- **Used by:** `PlatformInsightsController` in `src/Souq.API/Controllers/PlatformControllers.cs`, and `StoreReportsController` in `src/Souq.API/Controllers/StoreReportsController.cs`.
- **Enforced vs convention:**
  - Enforced — `TenancyRuleTests` allows `IgnoreQueryFilters` in exactly one type (`PlatformQueries`); adding a second is a security decision that fails the build first.
  - Enforced — `ModuleAndContractRuleTests` requires every request under `Features.Reporting` to implement `IAuditable`, exactly as for the platform area: a cross-store read is an event worth recording.
  - Convention — "Reporting never writes" is not enforced by a test. The port returns a DTO and the implementation only counts, but nothing would stop a future method from mutating.

## Data ownership

The module owns **no tables**. The platform query reads `Tenants`, `Users`, `Customers`, `Products` and `Orders`; the store dashboard reads `Orders` (and its owned items), `Products`, `Categories`, `ProductVariants` and `Customers`, all through the tenant filter. There are no read models, no materialized views and no caches: each request aggregates the live tables.

Counting semantics worth knowing before quoting a number:

- `TenantsByStatus` counts every store, including archived ones.
- `PlatformAccounts` counts users with no store, and `StoreStaffAccounts` counts `TenantAdmin` and `TenantStaff` across all stores — both include disabled accounts and unaccepted invitations.
- `Customers`, `Products` and `Orders` are raw row counts: erased customers, archived products and cancelled orders are all included.
- `OrdersLast30Days` counts by `CreatedAt`, in UTC.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/platform/stats` | `platform.reports.view`, platform host only (`[PlatformEndpoint]`) | — | Platform statistics |
| GET | `/api/admin/reports/dashboard` | `store.reports.view` | — | Store dashboard |

## Security and permissions

- `platform.reports.view` is granted to PlatformOwner and PlatformAdmin.
- `store.reports.view` is granted to TenantAdmin and TenantStaff, and now has its endpoint: `GET /api/admin/reports/dashboard`. The store is resolved from the host through `ITenantContext` — there is no tenant id in the route, the query string or the body, so a caller has nothing to change in order to read another store.
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
| Application | `tests/Souq.Application.Tests/Reporting/StoreDashboardTests.cs` | Window arithmetic for every range and the previous-period boundary |
| Integration | `tests/Souq.IntegrationTests/StoreDashboardTests.cs` | The store dashboard's numbers and its boundary: an unpaid order is not revenue, revenue and average order value, cross-store isolation, a foreign token, customer and anonymous refusal, staff access, an empty store, an unknown range, the audit row, and a translated category name rather than a slug |
| Integration | `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs` | That a PlatformAdmin can call `GET /api/platform/stats` (status only) |
| Integration | `tests/Souq.IntegrationTests/AuthorizationBoundaryTests.cs` | 404 on a store host, 401 for anonymous callers and for store tokens, and that the endpoint declares a permission |
| Frontend | `frontend/src/pages/admin/Dashboard.test.jsx`, `frontend/src/pages/admin/BusinessOverview.test.jsx`, `frontend/src/features/reporting/dashboardView.test.js`, `frontend/src/features/reporting/businessHealth.test.js`, `frontend/src/features/reporting/chartScales.test.js` | How the screens present a dashboard response they are given: net rather than gross revenue, no profit wording, alerts, the verdict and its reason, empty stores without NaN, the period key sent, text alternatives for charts |
| Frontend | `frontend/src/pages/platform/PlatformOverview.test.jsx` | How the overview presents a stats response: totals across every status, zero-filled statuses, the counting caveats on screen, an empty platform without NaN |
| Architecture | `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` | The bypass allowlist |
| Architecture | `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs` | Every Reporting request is auditable; the module's folder map |

**Gap:** the *platform* statistics still have no test of their numbers on the server (`PlatformOverview.test.jsx` feeds the screen a mocked response). A change to `PlatformQueries.GetStatsAsync` that quietly counted the wrong thing — archived stores, or orders in every status — would be caught by nothing. The store dashboard no longer has that gap.

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

**Platform statistics**

- No per-store breakdown and no time series: `OrdersLast30Days` is the only temporal figure and there is nothing to compare it to.
- Every call recounts the whole platform: seven aggregate queries over tables not indexed for this purpose, with no cache.
- Counts include archived, cancelled, disabled and erased rows, which is rarely what a dashboard wants to show. The platform overview says so on screen rather than leaving the reader to assume.
- No test asserts the numbers the server computes. The frontend overview test checks only how a mocked response is displayed.

**Store dashboard**

- **No profit and no margin.** Neither `Product` nor `ProductVariant` carries a cost price, so any margin would be invented. Both dashboards state this rather than omitting it quietly.
- **No conversion rate.** Nothing records visits or sessions, so there is no denominator.
- **No forecasting.** Every figure describes a period that has already ended.
- Each request runs several aggregate queries with no cache. That is right at this scale and will not be at a much larger one; [Dashboards.md](Dashboards.md) §7 records the measurements to argue from.
- Refunds are attributed to the **order's** period rather than the refund's. That is a deliberate choice, not a derivation ([Dashboards.md](Dashboards.md) §2).

## Future evolution

- **Delivered (roadmap Phase 17 territory):** the store dashboard — net revenue, counted orders, average order value, new customers, the trend, orders by status, best sellers, category performance, stock and customers. `store.reports.view` now has its endpoint.
- **Delivered (the reporting slice of roadmap Phase 18):** the platform overview is the first real consumer of `GET /api/platform/stats`. Per-store "health" was delivered by Phase 18 as handover readiness on `/platform/stores` (a domain, an administrator who accepted) — a [Platform](../Platform/README.md) list, not a report. No store's commercial figures reach the platform by design: the overview shows platform totals only, and there is no per-store drill-down.
- **PLANNED:** an export of a report (CSV or PDF), which is the most-requested thing neither dashboard does.
- **PLANNED (Phase 21):** the performance review, which is the natural moment to decide between caching and precomputation.
- **FUTURE:** event-fed read models, and a separate reporting store — [Architecture.md §9](../../02-ARCHITECTURE/Architecture.md#9-future-scaling-and-service-extraction) names Reporting as an extraction candidate precisely because it only reads.
