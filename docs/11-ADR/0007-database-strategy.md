# ADR-0007: Database strategy

- **Status:** Accepted, 2026-09-11. Principles apply from Phase 1A. Details: [DatabaseDesign.md](../06-DATABASE/DatabaseDesign.md).

## Context

- SQL Server 2022 with EF Core 10, code-first migrations.
- Phase 0 found: money in `decimal(18,2)`, no concurrency tokens, a missing self-FK on categories, nullable aggregate-child FKs, and stale hand-written SQL scripts.

## Problem

Which conventions should govern the schema so that correctness is enforced by the database as well as by the code, and so that future tenancy and extraction stay possible?

## Options considered and decisions

| Topic | Options | Decision |
|---|---|---|
| Source of truth | Hand SQL · EF migrations · both | **EF migrations only**; SQL is generated with `migrations script --idempotent` for review |
| Contexts | One `DbContext` · one per module | **One** (single unit of work for checkout). Module-owned configurations. Revisit on extraction. |
| Module schemas | `dbo` · schema per module | **`dbo` for now**, with an ownership table in the docs. Revisit when a module is extracted or per-module permissions are needed. |
| Primary keys | `int` identity · GUID v7 | **`int` identity**: no churn, compact. Public exposure uses order numbers, slugs, and random tokens. Merging tenants is not supported. |
| Money | `decimal(18,2)` · `(18,3)` · `(19,4)` · `money` | **`decimal(19,4)`**: holds every ISO-4217 exponent, including JOD's 3 ([ADR-0014](0014-money-precision.md)) |
| Concurrency | none · `rowversion` · pessimistic locks · atomic updates | **`rowversion`** on hot aggregates ([ADR-0013](0013-optimistic-concurrency.md)) |
| Deletion | global soft delete · per-aggregate status · hard | **Per-aggregate policy**: history is never deleted (orders, ledger, payments), restorable things are archived (products, coupons, users), everything else is hard-deleted |
| Uniqueness | code checks · DB constraints | **Both**: the code gives friendly messages, the database is the race-proof guard. Scoped per tenant from Phase 2. |
| Relationships | FKs everywhere · FKs within modules | **FKs within a module** (Restrict by default, Cascade for aggregate children). Across modules: an id plus a snapshot, except `TenantId`. |
| Timestamps | local · UTC `datetime2` · `datetimeoffset` | **UTC `datetime2`**, stamped by the context. Display converts to the tenant time zone. |
| Migration execution | at startup · deployment step | At startup for now. **Deployment step (migration bundle) in Phase 23**, before running more than one replica. |

## Why

- Each choice favours **correctness enforced by the database** plus **operational simplicity**, and keeps tenancy and extraction possible.
- Soft delete everywhere was rejected: global filters hide bugs, and "deleted" means different things for an order and for a coupon.

## Consequences

Phase 1A migration:
- widen money columns;
- add `RowVersion` to Products, Coupons, and Orders;
- add the `Categories.ParentId` FK;
- make `OrderItems.OrderId` and `OrderStatusHistories.OrderId` non-nullable;
- delete `database/*.sql`.

## Revisit when

- A module is extracted (it gets its own context and schema).
- Write contention on inventory needs atomic updates.
- More than one app replica runs (migrations move to deployment).
