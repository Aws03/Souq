# ADR-0022: Tenancy enforcement details

- **Status:** Accepted, 2026-09-11 (Phase 2). Refines [ADR-0005](0005-multi-tenancy-model.md) (shared database + `TenantId`) and [ADR-0006](0006-tenant-resolution.md) (host-based resolution) with the implementation choices those ADRs left open.
- **Date:** 2026-09-11
- **Related modules:** Platform; Identity (tokens bound to hosts); Cross-cutting (query filter, write guard, host resolution, storage keys)
- **Related ADRs:** refines [ADR-0005](0005-multi-tenancy-model.md) and [ADR-0006](0006-tenant-resolution.md); removes the default currency of [ADR-0014](0014-money-precision.md) and extends the storage keys of [ADR-0016](0016-upload-validation.md); relied on by [ADR-0024](0024-platform-administration.md), [ADR-0025](0025-catalog-model.md), [ADR-0027](0027-customer-profile-and-erasure.md), [ADR-0028](0028-basket-and-pricing-pipeline.md), [ADR-0033](0033-review-moderation-and-wishlist.md), [ADR-0034](0034-notifications-outbox.md) and [ADR-0035](0035-white-label-runtime.md)

## Context

ADR-0005 and ADR-0006 fixed the model: one database, a `TenantId` on every tenant-owned row, the tenant resolved from the host, and a `tid` claim that must match the host. Implementing them required further decisions. Each one affects security, so they are recorded here instead of being left implicit in code.

## Problem

Which concrete mechanisms implement the shared database with a tenant id, and the tenant resolved from the host, that the two earlier ADRs decided? Every one of these choices affects isolation, so leaving them implicit in the code would hide security decisions.

## Options considered

Alternatives were not recorded when this decision was made. The model-level options are in [ADR-0005](0005-multi-tenancy-model.md) (isolation model) and [ADR-0006](0006-tenant-resolution.md) (where the tenant comes from); this ADR records the implementation choices those two left open. The Decisions below name the approaches they rule out:

- switching the tenant inside a request, instead of opening a new scope (Decision 1);
- returning every tenant's rows when no tenant is set (Decision 2);
- letting a handler or a client choose the tenant on insert (Decision 3);
- a host fallback in Production, or development conveniences enabled from configuration (Decision 5);
- keeping the default of 1 on the tenant columns after the backfill (Decision 11).

## Decisions

1. **The tenant context is set once per service scope.**
   - `TenantContext` is a concrete Application class. The resolution middleware, background jobs, and seeding call it; use cases only see the read-only `ITenantContext`. An architecture test forbids features from taking `TenantContext`.
   - Setting it twice throws. Working in another tenant always means a **new** scope (`TenantScopes.RunAsync`), never switching mid-request.
2. **One named query filter, applied by reflection.**
   - Every `ITenantOwned` entity gets the filter `"Tenant"` (`TenantId == CurrentTenantId`) plus an FK to `Tenants`.
   - `CurrentTenantId` is read when each query executes. With no tenant it **throws**, including in platform scope; it never returns every tenant's rows.
   - The only permitted bypass is `IgnoreQueryFilters`, inside a reviewed allowlist of platform query types. An architecture test scans the IL to enforce this, and another forbids raw SQL outside migrations.
3. **A write guard in the unit of work.**
   - `TenantWriteGuardInterceptor` stamps `TenantId` on insert. Entities have no setter for it, so neither handlers nor clients can choose it.
   - It rejects any insert, update, or delete of another tenant's row, and any change to `TenantId`. Rejection throws `CrossTenantWriteException`, which becomes a 500 plus a Critical security log.
4. **References between tenant-owned rows carry the tenant.**
   - Foreign keys are composite, `(TenantId, XId) → (TenantId, Id)`, through alternate keys on Categories, Products, Customers, and Orders. The database itself makes a cross-tenant reference impossible, even if a handler forgets its existence check.
   - Two exceptions: aggregate children with shadow keys to their root (created together in one scope), and the optional category parent (validated in the handler).
   - FK violations (SQL 547) become `409 ReferenceConflict`.
5. **Host resolution order.**
   - First, `PlatformHosts` puts the request in platform scope.
   - Otherwise:
     - In Development/Testing only: the `X-Tenant` header, then the `TenantDomains` map, then `localhost` → `Tenancy:LocalDefaultTenant`, then `{slug}.localhost`.
     - In Production: the `TenantDomains` map only, with no fallback. An unknown host gets `404 StoreNotFound`.
   - Development conveniences are switched on from the environment in `Program.cs`; configuration cannot enable them.
   - Demo stacks bind hosts to the default store explicitly with `Seed:DefaultTenantHosts` (the Docker stack uses `localhost`).
6. **Status gating happens after routing, in one middleware.**
   - A platform endpoint (`[PlatformEndpoint]`) on a tenant host, or a tenant endpoint on the platform host, returns 404.
   - A `Provisioning` store serves auth and admin (permission) endpoints.
   - `Suspended` and `Archived` stores serve only endpoints marked `[AvailableWhenStoreClosed]`.
   - Everything else gets `503 StoreUnavailable`.
7. **Tokens are bound to hosts.**
   - Tenant tokens carry `tid`. `JwtBearerEvents.OnTokenValidated` fails authentication when `tid` does not match the resolved tenant; in platform scope the token must have no `tid`.
   - The result is a 401 for protected endpoints and anonymous access for public ones.
8. **The tenant directory cache is in-process and bounded.**
   - It is a dedicated `MemoryCache`: a 10,000-entry size limit (the host header is attacker-controlled), hits kept 60 seconds, misses 15 seconds.
   - `Invalidate()` bumps a generation number, which clears all entries at once.
   - With several instances, a tenant status change can take up to 60 seconds to reach the others. Revisit with a distributed cache or pub/sub invalidation when the app scales out.
9. **Currency is explicit.**
   - `Money` has no default currency any more.
   - New prices, coupon thresholds, and orders use the tenant's currency. `Order.Currency` is a snapshot.
   - The coupon preview ignores any currency the client sends.
10. **Storage is tenant-prefixed.**
    - Files are stored under `tenants/{id}/{folder}/{guid}.{ext}`. The resolution middleware serves `/uploads/tenants/{id}/…` only on that tenant's host (or the platform host).
    - Pre-Phase-2 files under `/uploads/{folder}/…` belong to the default store and stay public: they are catalog media with unguessable names.
11. **Migration strategy.**
    - The migration creates the default tenant with the fixed id 1 ("Marka Demo", P-04).
    - Each `TenantId` column is added with default 1, which backfills every existing row atomically. The default constraint is then dropped, so a later insert without an explicit tenant fails instead of silently landing in store 1.
    - The migration was rehearsed on real SQL Server with Phase 1 data (`MigrationRehearsalTests`).

## Consequences

- Isolation is structural: forgetting a filter, a guard, or an existence check is caught by the database, the architecture tests, or the isolation suite (`TenantIsolationTests`). Each has an explicit endpoint table and a completeness check.
- Every new tenant-owned entity must implement `ITenantOwned`, and every reference between tenant rows must use a composite key. The architecture tests fail otherwise.
- Platform features that read across tenants (Phase 4) must live in a reviewed `PlatformQueries` type and be audited.

## Revisit when

- The app runs on more than one instance and 60-second tenant-status staleness becomes unacceptable.
- A client requires physical isolation. The hybrid database-per-tenant path is described in [MultiTenancy.md §6](../02-ARCHITECTURE/MultiTenancy.md).
- SQL Server Row-Level Security is adopted as extra defence in depth (Phase 20).
