# ADR-0008: CQRS strategy

- **Status:** Accepted, 2026-09-11
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (the shape of every command and query); Catalog (the reference listing)
- **Related ADRs:** builds on [ADR-0001](0001-target-architecture.md) and [ADR-0003](0003-clean-hexagonal-boundaries.md) (query ports); projections applied by [ADR-0025](0025-catalog-model.md) (catalog), [ADR-0027](0027-customer-profile-and-erasure.md) (customers) and [ADR-0033](0033-review-moderation-and-wishlist.md) (review aggregates); query services are tested as decided in [ADR-0015](0015-testing-strategy.md)

## Context

- Commands and queries are already separate MediatR requests.
- Queries load full entities through repositories, then map them to DTOs in memory. That over-fetches, and `SearchAsync` has 8 parameters (Phase 0 D3).
- About 15 admin listings, storefront search, and dashboards are coming.

## Problem

How much read/write separation does Souq need, and is MediatR worth keeping?

## Options considered

- **L0:** no separation (services with mixed methods).
- **L1:** separate command and query handlers over one model and one database. This is the current state.
- **L1 + projection query services:** commands go through aggregates; queries go through Infrastructure services that project straight into DTOs.
- **L2:** dedicated read models (denormalized tables or views) maintained on write.
- **L3:** separate read stores (replica or search index), eventually consistent.
- Replace MediatR with plain application services.

## Decision

- **L1 + projection query services now.**
  - Query interfaces live in Application (for example `ICatalogQueries`), with EF projections in Infrastructure (`AsNoTracking`, `Select`).
  - Paging, sorting, and filtering helpers are shared.
- **L2 only for reporting and dashboards**, when agreed latency targets are missed.
- **L3 only for search**, if SQL search stops being enough.
- **Keep MediatR** (pinned to 12.x, Apache-2.0).
  - Its value is the pipeline: validation today; tenant/module checks, audit, and performance logging later.
  - Plus a uniform use-case shape.
  - It is **not** used for module-to-module calls or domain events.

## Why

- Projections fix over-fetching and repository-method explosion while keeping the Application layer EF-free.
- Read models add synchronization and consistency cost that is only worth paying for aggregate-heavy reports.
- Dropping MediatR would lose the pipeline, and we'd rebuild the same thing by hand.

## Consequences

- The Products listing becomes the reference implementation in Phase 1B. Other listings follow as their modules are rebuilt.
- Query services are tested against real SQL Server (integration tests), because their behaviour *is* SQL.

## Revisit when

- Dashboard queries exceed targets (→ L2).
- Catalog search needs relevance, facets, or typo tolerance (→ L3).
- The MediatR license or maintenance changes (→ a thin in-house dispatcher; the pipeline concept stays).
