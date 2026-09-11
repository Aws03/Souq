# ADR-0005: Multi-tenancy model

- **Status:** Accepted, 2026-09-11. Implementation: Phase 2.
- **Date:** 2026-09-11
- **Related modules:** Platform; Cross-cutting (every tenant-owned table)
- **Related ADRs:** companion of [ADR-0006](0006-tenant-resolution.md) (where the tenant comes from); refined by [ADR-0022](0022-tenancy-enforcement.md) (filter, write guard, composite keys); the audited platform bypass is built by [ADR-0024](0024-platform-administration.md)

## Context

Many independent stores run on one codebase. The business model is many small clients (≈ $5k each), there are no known physical-isolation requirements yet, and platform-wide dashboards are required. Details: [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md).

## Problem

How should tenant data be isolated so that cross-tenant access is impossible by default, without multiplying operational cost per tenant?

## Options considered

| Option | Isolation | Ops cost | Cross-tenant reporting | Verdict |
|---|---|---|---|---|
| Shared DB + shared schema + `TenantId` | Logical, enforced by the app (+ optional RLS) | Lowest | Trivial | ✅ Start here |
| Schema per tenant | Stronger | High (N schema migrations) | Harder | ❌ The costs of both, the benefits of neither |
| Database per tenant | Strongest | Highest (N migrations, backups, connections) | Hard | ⏸ As an opt-in hybrid for enterprise tenants |

## Decision

A shared database with `TenantId` on every tenant-owned table, enforced **centrally**:
- EF Core global query filters on every `ITenantOwned` entity;
- a SaveChanges write guard that stamps the tenant on insert and rejects cross-tenant modification;
- tenant-scoped unique indexes;
- an explicit, audited platform bypass.

A resolver seam (`ITenantDatabaseResolver`) keeps a **hybrid** model possible: individual tenants on dedicated databases with the same schema.

## Why

- It keeps the cost per tenant near zero, with one migration and one backup.
- It makes platform statistics simple.
- The main risk is a forgotten filter. It is removed structurally: the filter is automatic, the write guard catches bugs, queries without a tenant context throw, and the isolation test suite runs against real SQL Server.

## Consequences

- Every tenant-owned table gains `TenantId`, and indexes lead with it.
- Raw SQL is confined to reviewed Infrastructure query services.
- Tenant export and move stay possible (`WHERE TenantId = @id`); merging tenants is not supported.
- Noisy-neighbour risk is mitigated with indexes, per-tenant rate limits, and the dedicated-database option.

## Revisit when

- A contract or regulation demands physical isolation or data residency.
- One tenant consumes more than about 30% of database resources.
- Tenant count or size makes a shared database the bottleneck, even after read replicas.
