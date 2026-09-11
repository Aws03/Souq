# Architecture Decision Records

An ADR captures **one** significant decision: the context, the options considered, the choice, why it was made, its consequences, and **when to revisit it**. ADRs are immutable once accepted. A changed decision gets a new ADR that supersedes the old one.

| # | Decision | Status |
|---|---|---|
| [0001](0001-target-architecture.md) | Target architecture: modular monolith + Clean/Hexagonal + selective DDD + vertical slices + selective CQRS | Accepted |
| [0002](0002-modular-monolith-structure.md) | Modules as namespaces across the four layer projects, enforced by architecture tests | Accepted |
| [0003](0003-clean-hexagonal-boundaries.md) | Clean dependency rule + ports and adapters only at real variation points | Accepted |
| [0004](0004-module-boundaries.md) | 13 business modules; contracts, IDs, and a shared transaction for cross-module work | Accepted |
| [0005](0005-multi-tenancy-model.md) | Shared database + `TenantId` + central enforcement; hybrid path kept open | Accepted (implemented Phase 2) |
| [0006](0006-tenant-resolution.md) | Tenant resolved from the host; token `tid` must match; platform on its own host | Accepted (implemented Phase 2) |
| [0007](0007-database-strategy.md) | SQL Server + EF Core, one DbContext, int keys, per-tenant uniqueness, explicit soft-delete policy | Accepted |
| [0008](0008-cqrs-strategy.md) | CQRS level 1 + projection query services; read models only for reporting | Accepted |
| [0009](0009-ddd-usage.md) | Tactical DDD only where invariants are rich; small aggregates | Accepted |
| [0010](0010-authentication-authorization.md) | Evolve the custom JWT/BCrypt into a User aggregate + permissions + rotating refresh tokens | Accepted (implementation Phase 3) |
| [0011](0011-white-label-architecture.md) | Configuration-driven white-label: storefront config API + semantic tokens + presets | Accepted (Phase 4/15) |
| [0012](0012-service-extraction-strategy.md) | Monolith first; extraction only with evidence, prepared by contracts and an outbox | Accepted |
| [0013](0013-optimistic-concurrency.md) | `rowversion` optimistic concurrency with 409s and idempotent re-reads | Accepted (implemented 1A) |
| [0014](0014-money-precision.md) | `decimal(19,4)` + currency minor units enforced by `Money` | Accepted (implemented 1A) |
| [0015](0015-testing-strategy.md) | Testcontainers integration tests, NetArchTest architecture tests, AwesomeAssertions, Vitest | Accepted (implemented 1A) |
| [0016](0016-upload-validation.md) | Content-sniffed uploads with server-chosen extensions and a locked-down static file server | Accepted (implemented 1A) |
| [0017](0017-error-contract.md) | RFC 7807 ProblemDetails with typed errors, stable codes and one status mapping | Accepted (implemented 1B) |
| [0018](0018-observability.md) | Built-in structured logging, W3C trace id as correlation id, request line and log scopes | Accepted (implemented 1B) |
| [0019](0019-authorization-foundation.md) | `ICurrentUser`, permission policies, ownership in use cases, explicit auth on every endpoint | Accepted (implemented 1B) |
| [0020](0020-configuration-and-secrets.md) | Typed validated options, fail-fast startup, no implicit dev fallbacks outside Development | Accepted (implemented 1B) |
| [0021](0021-transaction-boundaries.md) | Use case owns the unit of work; no transaction spans a network call; compensation and outbox | Accepted (1B) |
| [0022](0022-tenancy-enforcement.md) | Tenancy enforcement details: set-once context, named filter that throws without a tenant, write guard, tenant-scoped composite FKs, dev-only resolution, status gating | Accepted (implemented Phase 2) |
| [0023](0023-sessions-and-credentials.md) | Session and credential mechanics: host-bound `tid` (none on platform hosts), `cid` claim, one role per account, 10 s refresh grace, 30-day sliding refresh, cached stamp check, email links on the request host, `host|IP` rate limits, id-preserving identity migration | Accepted (implemented Phase 3) |
| [0024](0024-platform-administration.md) | Platform administration: settings as a validated JSON document (WCAG contrast), module flags in the cached tenant snapshot, audit staged into the handler's unit of work (append-only), platform writes through the target store's scope, one reviewed cross-tenant query class, invitations on the store domain | Accepted (implemented Phase 4) |
| [0025](0025-catalog-model.md) | Catalog model: translation tables per aggregate, one default variant per product (SKU, price, compare-at), Draft/Active/Archived with no hard delete, per-store slugs, an ordered gallery, a guarded category tree, a data-preserving migration | Accepted (implemented Phase 5) |
| [0026](0026-inventory-reservations.md) | Inventory: an item per variant (on hand, reserved, `rowversion`), explicit reservations (reserve at checkout, commit at payment, release or restock on cancel), retry-from-fresh-read on conflicts, delta corrections with a reason, a hosted checkout-expiry sweep that asks the gateway first, module contracts enforced by tests | Accepted (implemented Phase 6) |

**Template:** Context · Problem · Options considered · Decision · Why · Consequences · Revisit when.
