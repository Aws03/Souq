# Architecture Decision Records

> **What an ADR is here:** the record of one decision — its context, the problem, the options that were considered, what was chosen, and what that costs. ADRs explain **why** the code looks the way it does. They are the first thing to read before changing an architectural rule, and the last thing to write after making one.
> **They are historical records.** An ADR is not updated when the code moves on; a later ADR supersedes it, and the living documents in `docs/02-ARCHITECTURE`, `docs/04-MODULES` and the rest describe today. Where an old ADR's description of the code has drifted, §4 lists it explicitly so nobody is misled.

## 1. How to use this index

- **Looking for a rule?** Find the category in §2, read the one-line decision, open the ADR only if you need the reasoning.
- **About to change something architectural?** Find the ADR that decided it. If your change contradicts it, you need a new ADR that supersedes it — not a quiet edit.
- **Writing one?** Copy the structure of a recent ADR (0031–0035 are good models): metadata (`Status`, `Date`, `Related modules`, `Related ADRs`), then `Context`, `Problem`, `Options considered`, `Decision`, `Consequences`, and optionally `Why`, `Revisit when`, `Migration`, `Verification`. `DocumentationTests` fails the build if an ADR is missing a required part or is not listed here.
- **Validity** in the tables below means: does the ADR still describe the code? *Accurate* / *Partially stale* (the decision holds, some details moved — see §4) / *Superseded in part* (a later ADR replaced part of it).

**When a change needs an ADR:** a new dependency or technology; a new module or a change to module boundaries; a different persistence, transaction or consistency strategy; anything touching tenant isolation, authentication or payment flow; a public contract that others will depend on; or deliberately *not* doing something that a future engineer would otherwise assume was an oversight.

## 2. Decisions by category

### Architecture and structure

| ADR | Decision | Validity |
|---|---|---|
| [0001](0001-target-architecture.md) | Modular monolith with the Clean dependency rule, selective DDD and CQRS, events only at the edges; no event sourcing, no microservices | Accurate |
| [0002](0002-modular-monolith-structure.md) | Modules are namespaces and folders inside the four layer projects, enforced by architecture tests — not separate projects | Partially stale (§4) |
| [0003](0003-clean-hexagonal-boundaries.md) | Strict layer dependencies; a port only at a real variation point; provider exceptions translated in the adapter | Partially stale (§4) |
| [0004](0004-module-boundaries.md) | Thirteen capability modules; call another module only through its contracts; reference by id; one shared transaction is allowed; no cycles | Partially stale (§4) |
| [0008](0008-cqrs-strategy.md) | Separate command and query paths over one database, with projection query services; read models only when reporting demands them | Accurate |
| [0009](0009-ddd-usage.md) | Tactical DDD only where invariants are rich; small aggregates; strategic DDD for boundaries | Partially stale (§4) |
| [0017](0017-error-contract.md) | One RFC 7807 error shape with a stable `code` and a trace id; results for decided outcomes, domain exceptions for guarded rules | Accurate (one stale example) |
| [0021](0021-transaction-boundaries.md) | The use case owns the unit of work; no transaction spans a network call; compensate or resolve idempotently | Partially stale (§4) |

### Multi-tenancy

| ADR | Decision | Validity |
|---|---|---|
| [0005](0005-multi-tenancy-model.md) | Shared database with a tenant id on every tenant-owned table, enforced centrally, with an audited platform bypass | Partially stale (§4) |
| [0006](0006-tenant-resolution.md) | The store comes from the Host header through a cached domain map; the token's tenant claim must match; the platform host is separate | Accurate |
| [0022](0022-tenancy-enforcement.md) | Scope-set tenant context, one named query filter, a write guard, composite foreign keys, status gating, host-bound tokens, tenant-prefixed storage | Accurate |
| [0024](0024-platform-administration.md) | Settings as one JSON document, modules as a column, audit staged inside the handler's transaction, one reviewed cross-tenant query type | Accurate |

### Security and identity

| ADR | Decision | Validity |
|---|---|---|
| [0010](0010-authentication-authorization.md) | A `User` aggregate with rotating refresh tokens and permission policies over built-in roles, instead of an identity framework | Accurate |
| [0016](0016-upload-validation.md) | Detect the file type from magic bytes, let the server choose the stored extension, cap sizes, lock down static file serving | Accurate |
| [0019](0019-authorization-foundation.md) | `ICurrentUser`, permissions as constants with one role→permission map, `[HasPermission]` policies, ownership in use cases, 404 for someone else's resource | Partially stale (§4) |
| [0023](0023-sessions-and-credentials.md) | Short access token, rotating hashed refresh cookie with a grace window, security stamp, lockout, host-bound tenant claim, partitioned rate limits | Accurate |

### Data

| ADR | Decision | Validity |
|---|---|---|
| [0007](0007-database-strategy.md) | EF Core migrations as the only schema source, one `DbContext`, integer keys, fixed money precision, rowversion on hot aggregates | Accurate |
| [0013](0013-optimistic-concurrency.md) | Rowversion on contended aggregates; conflicts become 409 | Superseded in part by [0026](0026-inventory-reservations.md) |
| [0014](0014-money-precision.md) | `decimal(19,4)` storage, a `Money` value object carrying the currency, commercial rounding, provider conversion in the adapter | Accurate (two stale details, §4) |
| [0025](0025-catalog-model.md) | Translation tables, exactly one default variant per product, a status lifecycle, per-store slugs, bounded gallery and category depth | Accurate |
| [0026](0026-inventory-reservations.md) | Stock per variant, explicit reservations, an append-only ledger, bounded retry, and a sweeper for abandoned checkouts | Accurate (one stale expectation, §4) |
| [0027](0027-customer-profile-and-erasure.md) | Addresses inside the customer aggregate; Active/Blocked; erasure anonymizes in place and keeps orders | Accurate (one stale detail, §4) |
| [0028](0028-basket-and-pricing-pipeline.md) | A server-side basket with a hashed guest cookie, live prices, and one pricing pipeline shared with checkout; baskets never reserve stock | Partially stale (§4) |
| [0029](0029-orders-lifecycle.md) | Per-store order numbers, a random public tracking token, totals frozen at placement, one transition table, the actor recorded | Accurate |
| [0030](0030-coupon-redemptions.md) | A coupon use is reserved inside the checkout transaction, confirmed at payment and released on every cancellation | Accurate |
| [0032](0032-shipping-methods.md) | Store-defined methods behind a rate-provider contract, chosen at checkout and snapshotted on the order | Accurate |
| [0033](0033-review-moderation-and-wishlist.md) | Moderation states under a per-store auto-approve policy; aggregates over approved reviews only; a server-side wishlist that absorbs the guest list | Accurate (one stale expectation, §4) |

### Payments

| ADR | Decision | Validity |
|---|---|---|
| [0031](0031-payments-and-refunds.md) | One payment per order; refunds as reserve → call → record with an idempotency key; per-store gateway accounts with encrypted secrets; webhooks routed by intent metadata | Superseded in part by [0036](0036-payment-intent-state-machine.md); **P-05** (JOD minor units) and **D-13** (account model) remain open |
| [0036](0036-payment-intent-state-machine.md) | A confirmation reads the intent's state instead of a boolean: a retryable decline leaves the order open, only a dead intent cancels it, and money captured after an order closes is recorded so it can be refunded | Accurate; the state mapping is **unverified against a real Stripe account** |

### Frontend and white-label

| ADR | Decision | Validity |
|---|---|---|
| [0011](0011-white-label-architecture.md) | One build, configuration-driven branding; the platform owner controls contracts, the tenant controls presentation; no arbitrary CSS or scripts | Partially stale (§4) |
| [0035](0035-white-label-runtime.md) | The SPA boots from the storefront configuration, writes semantic tokens, gates modules, and splits four areas with lazy routes; D-19 deferred with a trigger | Superseded in part by [0037](0037-frontend-server-state-and-types.md), which takes the D-19 decision it deferred |
| [0037](0037-frontend-server-state-and-types.md) | D-19 decided: a query library (TanStack Query) is the target for server state, adopted at the first screen rebuilt rather than installed now; TypeScript deferred until a CI pipeline exists to enforce it | Completed by [0038](0038-query-layer-adopted-and-type-checking.md), which takes both adoptions at the points this record named |
| [0038](0038-query-layer-adopted-and-type-checking.md) | Both of ADR-0037's triggers fired: TanStack Query is installed and the storefront read paths migrated, with the cache reset on any identity change; types arrive as `checkJs` plus JSDoc on `.js` boundaries, enforced in CI, instead of a TypeScript conversion | Accurate |

### Operations

| ADR | Decision | Validity |
|---|---|---|
| [0018](0018-observability.md) | A W3C trace id as correlation id, one log line per request, log scopes, JSON console logging in Production | Accurate |
| [0020](0020-configuration-and-secrets.md) | Typed options validated at startup, fail-fast before touching the database, no silent development fallbacks | Superseded in part by [0023](0023-sessions-and-credentials.md) and [0034](0034-notifications-outbox.md) |
| [0034](0034-notifications-outbox.md) | A transactional outbox with a leased dispatcher and bounded retries; domain events written in the same save; localized branded email; in-app notifications | Accurate (provider list has grown, §4) |

### Testing

| ADR | Decision | Validity |
|---|---|---|
| [0015](0015-testing-strategy.md) | Four .NET suites plus Vitest; a real SQL Server through Testcontainers; architecture rules as tests | Accurate; its "CI" revisit is still open |

### Future scaling

| ADR | Decision | Validity |
|---|---|---|
| [0012](0012-service-extraction-strategy.md) | Stay a monolith; keep the rules that make extraction possible; pay for distribution only on evidence | Accurate |

## 3. What later ADRs changed

| Earlier decision | Changed by | What changed |
|---|---|---|
| [0013](0013-optimistic-concurrency.md) checkout conflict handling | [0026](0026-inventory-reservations.md) | A bounded retry then `422 InsufficientStock`, instead of a 409 "stock changed"; stock corrections became delta adjustments with a reason |
| [0020](0020-configuration-and-secrets.md) token lifetime | [0023](0023-sessions-and-credentials.md) | Access-token expiry validated 5–60 minutes |
| [0020](0020-configuration-and-secrets.md) missing email provider | [0034](0034-notifications-outbox.md) | Startup now refuses outside Development/Testing unless `Email:Provider=Log` is explicit |
| [0005](0005-multi-tenancy-model.md) model | [0022](0022-tenancy-enforcement.md) | Filled in the enforcement details (filter name, write guard, composite keys, status gating) |
| [0010](0010-authentication-authorization.md) roles | [0023](0023-sessions-and-credentials.md), [0024](0024-platform-administration.md) | Platform and store roles, invitations, account status |
| [0028](0028-basket-and-pricing-pipeline.md) checkout source and stages | [0029](0029-orders-lifecycle.md), [0032](0032-shipping-methods.md) | Checkout reads the basket server-side; shipping became a charged stage |
| [0021](0021-transaction-boundaries.md) residual risks | [0026](0026-inventory-reservations.md), [0034](0034-notifications-outbox.md) | The abandoned-checkout sweeper and the outbox closed both risks it listed |
| [0031](0031-payments-and-refunds.md) confirmation path | [0036](0036-payment-intent-state-machine.md) | A non-succeeded confirmation no longer cancels the order unconditionally: it reads the intent's state, and a capture against a closed order is recorded instead of ignored |
| [0035](0035-white-label-runtime.md) deferring D-19 with a trigger | [0037](0037-frontend-server-state-and-types.md) | The trigger fired without producing a decision, so D-19 was split and taken: a query library is the target for server state with a named adoption point, and TypeScript's trigger became "a CI pipeline exists" rather than a date that had already passed |
| [0037](0037-frontend-server-state-and-types.md) naming two adoption points | [0038](0038-query-layer-adopted-and-type-checking.md) | Both points arrived in Phase 16 — six storefront screens rebuilt, and a CI pipeline in place — so the library was installed and migrated, and the TypeScript question was answered with checked JSDoc rather than deferred a third time |

## 4. Statements inside ADRs that no longer match the code

The decisions stand; these *descriptions* have drifted. Living documents are authoritative — this table exists so a reader of an old ADR is not misled.

| ADR | What it says | What is true today |
|---|---|---|
| 0002 | Modules are namespaces in every layer (`Souq.Domain.<Module>`, `Souq.Infrastructure.*.<Module>`) | Only the Application layer has per-module folders, named after features and mapped by `ModuleMap`; the Domain is organized by kind (only Identity and Platform per module), Infrastructure by technical concern ([DependencyRules.md §5](../02-ARCHITECTURE/DependencyRules.md)) |
| 0003 | All ports live in `Application/Common/Interfaces`; a `FakePaymentService` stand-in | Ports are spread over `Common/Interfaces`, `Common/Notifications`, `Common/Security`, `Common/Tenancy`; the clock is .NET's `TimeProvider`; the payment stand-in is `FakeGateway` |
| 0004 | Modules communicate only through contracts | True in the Application layer and tested there; several modules still use another module's *domain* repositories directly ([TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md)) |
| 0005 | An `ITenantDatabaseResolver` seam keeps a per-store database possible | No such interface exists; the seam is the tenant directory, where a per-store connection would be injected |
| 0009 | Value objects `Money`, `Address`, `Slug`, `Email`, `Sku`; a pricing *domain* service | Value objects are `Money`, `PostalAddress`, `CatalogText`, `OrderActor`; slugs use the `CatalogSlug` helper; pricing is `PricingService` behind `IPricing` in the Application layer |
| 0011, 0002, 0015, 0020 | "A CI check", "will run in CI", CI in Phase 23 | There is no CI configuration in the repository. The white-label rule is enforced by tests that a person must run |
| 0011 | A tenant admin controls templates | There are no store-editable templates; ADR-0034 rejected them. A store picks typography and theme presets |
| 0013, 0017 | The `StockChanged` 409 code | It no longer exists; insufficient stock is `422 InsufficientStock` |
| 0014 | "The default currency is still JOD"; a short zero-decimal list | `Money` has required an explicit currency since Phase 2; `CurrencyInfo` lists many more zero-decimal currencies and two four-decimal ones |
| 0019 | `RolePermissions`: `Admin` → all | There is no `Admin` role; the map covers PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff and Customer |
| 0020 | Reset links logged in Development *and* Testing | Development only |
| 0021 | Multi-save atomicity "through the EF execution strategy" | `IUnitOfWork.InTransactionAsync` over an explicit transaction; no execution strategy, and retry-on-failure is off |
| 0026 | Low-stock alert *emails* arrive in Phase 14 | Phase 14 shipped the in-app `stock.low` notification only |
| 0027 | The order has no billing address yet | It has had one since Phase 9 |
| 0028 | Shipping and tax stay at zero | Shipping is charged since Phase 12; only tax is still zero (open decision P-06) |
| 0033 | Phase 14 will notify staff of pending reviews and customers of decisions | Not built; only order status, new order and low stock exist |
| 0034 | One email provider plus a log fallback | The chain is Resend → Brevo → Gmail SMTP by which key is present; the startup rule is unchanged |
| 0035 | D-19 trigger: "the start of Phase 16" | Taken in Phase 17 by [0037](0037-frontend-server-state-and-types.md): the query library is decided and its adoption point named, and the TypeScript trigger is re-worded to a condition that can actually fire (a CI pipeline exists). Note that the branch `phase/16-engineering-knowledge-and-handoff` is this documentation pass, not the roadmap's Phase 16 (Storefront) |

## 5. Numbering and lifecycle

- ADRs are numbered sequentially and never renumbered. The next one is **0038**.
- A superseded ADR keeps its text; its `Status` line says what replaced it, and the replacement links back through `Related ADRs`.
- Rejected proposals are worth an ADR too: "we considered X and chose not to" saves the next person the same investigation ([ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) collects the big ones).
