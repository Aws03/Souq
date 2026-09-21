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
| [0025](0025-catalog-model.md) | Translation tables, exactly one default variant per product, a status lifecycle, per-store slugs, bounded gallery and category depth | Superseded in part by [0039](0039-product-variants-order-identity.md) (the sellable unit: variants gain an active flag, and the option model is decided) |
| [0026](0026-inventory-reservations.md) | Stock per variant, explicit reservations, an append-only ledger, bounded retry, and a sweeper for abandoned checkouts | Accurate (one stale expectation, §4) |
| [0027](0027-customer-profile-and-erasure.md) | Addresses inside the customer aggregate; Active/Blocked; erasure anonymizes in place and keeps orders | Accurate (one stale detail, §4) |
| [0028](0028-basket-and-pricing-pipeline.md) | A server-side basket with a hashed guest cookie, live prices, and one pricing pipeline shared with checkout; baskets never reserve stock | Partially stale (§4) |
| [0029](0029-orders-lifecycle.md) | Per-store order numbers, a random public tracking token, totals frozen at placement, one transition table, the actor recorded | Accurate |
| [0030](0030-coupon-redemptions.md) | A coupon use is reserved inside the checkout transaction, confirmed at payment and released on every cancellation | Accurate |
| [0032](0032-shipping-methods.md) | Store-defined methods behind a rate-provider contract, chosen at checkout and snapshotted on the order | Accurate |
| [0033](0033-review-moderation-and-wishlist.md) | Moderation states under a per-store auto-approve policy; aggregates over approved reviews only; a server-side wishlist that absorbs the guest list | Accurate (one stale expectation, §4) |
| [0039](0039-product-variants-order-identity.md) | P-08 decided (up to 3 options, 20 values, 100 variants; "From" the lowest purchasable price; explicit choice with sold-out values disabled). V1 built: order lines record the variant with label and SKU snapshots and merge by variant, the exact historical backfill, variant identity through basket, pricing and checkout with `VariantRequired`, variants deactivated never deleted, and variant-keyed stock administration | Accurate; V2 built in [0040](0040-product-option-model.md); V3 (storefront) not built |
| [0040](0040-product-option-model.md) | V2 built: options (≤3) and values (≤20) with per-language names unique per product/option; variants as combinations (≤100, inactive counted) with a database-unique combination key; the first option converts the default variant in place; a used value can't be removed; a movable, always-active default; product-level pricing refused on products with options; a root concurrency guard; merchant admin page and per-variant inventory screen; the storefront shows only products with one active variant until V3 | Accurate; its temporary storefront gate was removed by [0041](0041-storefront-variant-selection.md) |
| [0041](0041-storefront-variant-selection.md) | V3 built: the storefront gate removed; `ProductDto` carries options and active variants with availability; the displayed price is the cheapest **purchasable** variant with a "From" flag, and filters, sorting and structured data use the same number; deactivated variants are hidden from shoppers while sold-out values are shown disabled; a product with everything sold out stays listed as unavailable; an explicit choice with no silent switching and no reachable impossible combination; labels live per language in the cart and frozen on the order and its email | Accurate |
| [0042](0042-local-search-engine.md) | M3 built: catalog text is stored in a normalized form (`SearchText.Normalize` — Arabic diacritics, tatweel, alef/ة/ى folding, Arabic-Indic digits, case, punctuation) written by the single `CatalogTranslation.Apply` write path and indexed per store; every query word is a separate condition matched in name, description or **category** name in any language; `ProductSortBy.Relevance` scores in SQL; a bounded Damerau–Levenshtein distance is the deterministic typo primitive. **Full-Text Search was measured unavailable in the pinned SQL Server image (Msg 7609) and would not have given per-store synonyms or typo tolerance anyway**; no search service and no runtime AI, on measured evidence (the indexed path is ~72× faster than the substring scan it replaces) | Accurate |
| [0043](0043-row-level-security-evaluation.md) | M15 evaluated SQL Server Row-Level Security as defence in depth for tenant isolation and **did not adopt it**, with the reasoning written out: every path around the application filter — `IgnoreQueryFilters`, raw SQL, bulk writes, a new id-bearing endpoint — is already a failing build, so RLS would guard a class of mistake that cannot reach `main`; its per-connection session value is a silent footgun under EF's connection pooling (set late ⇒ read the previous request's store; unset ⇒ an empty catalogue with no error); the platform area must read across stores by design, so the policy would need a mode switch and four table exemptions — a second copy of the application's own rules in another language; and the application connects as `sa`, which can disable a policy anyway, so least-privilege logins are the cheaper and prerequisite investment. Lists the three conditions that void the decision | Accurate |
| [0044](0044-caching-evaluated-not-adopted.md) | M16 evaluated caching with measurement and **did not add a layer**: every read route already meets its p95 target (catalogue 56 ms p50, dashboard 43 ms), and under load the API sits at 0.54% CPU while SQL Server runs at 47% — so the constraint is database capacity, not read latency. The candidates do not cache well either: the most-written table is the search log (a read cache does nothing for writes), the dashboard's aggregates change with every order (a correct TTL has no hit rate, a hitting TTL shows stale money), and the catalogue's key space is per-store/page/filter/sort/language. Against that, every cache key here is a tenancy question — a key that forgets `TenantId` is a data leak with a latency benefit. The one pathological read found was fixed by reordering an index (25,375 → 107 logical reads), which a cache would have hidden rather than corrected | Accurate |
| [0045](0045-production-edge-and-observability-stack.md) | M17 drew the line between what the repository decides about the production edge and what a deployment owns. **Metrics are instrumented now with `System.Diagnostics.Metrics`** — no new dependency, four counters chosen against one test (*would this wake a person?*), high-cardinality tags deliberately excluded — while the **exporter waits for a destination**, which is what ADR-0018 actually deferred. Traces are not instrumented because the W3C trace id is already in every log line and every error body. **TLS**: the behaviour (HSTS, forwarded-proto trust and its two startup diagnostics, both CSPs) is owned here; the terminator and certificate are not, and automated TLS for merchant domains needs an edge that can answer an ACME challenge — the `TenantDomains` data it would read already exists. **CDN deferred on M16's measurement** (the API was at 0.54% CPU under load; serving assets is not the shortage). Ends with what makes each remaining item a configuration change rather than a code change | Accurate |
| [0046](0046-continuous-delivery-and-rollback.md) | M18 built the CD half `ci.yml` never had. **A release is a SemVer tag**; images carry both the version and the commit, and `docker-compose.yml` now names them — deploying by building from whatever source is present is unrollbackable by construction, because what was running a minute ago never had a name. **The migration bundle is a release artifact** built in the pipeline, not on the host, which is what makes R-18's *migrate, verify, roll out* usable — rehearsed by stepping the schema back and forward. **Rollback is conditional on the schema:** automatic when it did not move, and a deliberate refusal plus the restore path when it did or cannot be read, because an old image on a newer schema is worse than the outage being escaped. Ends with the two things that have not run: the SSH step (no server) and the pipeline itself (GitHub Actions billing-blocked) | Accurate |

### Payments

| ADR | Decision | Validity |
|---|---|---|
| [0031](0031-payments-and-refunds.md) | One payment per order; refunds as reserve → call → record with an idempotency key; per-store gateway accounts with encrypted secrets; webhooks routed by intent metadata | Superseded in part by [0036](0036-payment-intent-state-machine.md); **P-05** (JOD minor units) and **D-13** (account model) remain open |
| [0036](0036-payment-intent-state-machine.md) | A confirmation reads the intent's state instead of a boolean: a retryable decline leaves the order open, only a dead intent cancels it, and money captured after an order closes is recorded so it can be refunded | Accurate; the state mapping is **unverified against a real Stripe account** |

### Commercial platform (designed, not built)

These records decide *where* and *how* the commercial layer will be built. The first six were written before any
of it existed; `0053` and `0054` were implemented by `C1` and `C2`, and `0055` records the tax capability the
owner decided on 2026-09-21 and is not implemented yet. The architecture they belong to is
[CommercialPlatformArchitecture.md](../12-ROADMAP/CommercialPlatformArchitecture.md) and the sequence is
[CommercialPlatformPlan.md](../12-ROADMAP/CommercialPlatformPlan.md). None of them decides a commercial question
that is the owner's — those stay in [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md).

| ADR | Decision | Validity |
|---|---|---|
| [0047](0047-commercial-control-plane.md) | The commercial layer is a control plane in the platform scope of the same deployable; every commercial table takes one of three shapes the tenancy tests already enforce; a fourteenth module; entitlements feed the existing module seam rather than adding a second check, and its fail-open default is closed on the way in | Accepted as the design; not implemented |
| [0048](0048-payment-provider-abstraction.md) | The payment port becomes flow-agnostic: one start verb returning a discriminated result (redirect, client script, browser post, synchronous, deferred out-of-band), a persisted payment attempt with an opaque provider bag, totals derived from an append-only event log, a webhook inbox verified over raw bytes and routed by a tenant in the path, and adapter capabilities declared as data | Accepted as the design; not implemented, and gated on **D-13** and the launch-provider choice |
| [0049](0049-tenant-quota-enforcement.md) | A per-tenant quota is a counter row taken under an explicit lock inside the caller's transaction — **not** the write-first-then-recount pattern used elsewhere here, which depends on an isolation-level invariant nothing asserts (TD-68) and whose own test cannot reach the guarded branch | Accepted as the design; not implemented. Decides how TD-68 closes |
| [0050](0050-behavioural-event-foundation.md) | Behavioural events are captured now with the fields that cannot be reconstructed later (rank, list identity, a search-execution id echoed back, write-time denormalisation); the same non-blocking bounded-channel pattern as the search log; the identity link kept in a separate table; roll up before purging | **Accepted and implemented (C9, 2026-09-22).** `C-08` was answered A, and the three sub-answers it reserved to the owner are why capture ships **off until configured** — `Enabled` without a retention period and a lawful basis refuses to boot |
| [0051](0051-custom-domain-lifecycle.md) | A custom domain carries two separate state machines — ownership and certificate — behind an attachment port that fits both a managed edge and a self-run ACME client, plus a DNS probe; the serving gate reads ownership and is a security control | Accepted as the design; not implemented. Makes [0045](0045-production-edge-and-observability-stack.md)'s "needs a real edge" concrete |
| [0052](0052-bounded-extension-model.md) | Customer-specific extension is integration, not execution: bounded configuration first, then outbound webhooks over the existing outbox, then a scoped API — and anything else is product-ized or declined. Deliberately does **not** meet ExplicitNonGoals §14's condition for revisiting per-tenant custom code | Accepted as the design; not implemented |
| [0053](0053-entitlement-resolution.md) | An entitlement is the **intersection** of what the plan grants (plus live overrides) and what the platform has switched on, composed once in the tenant snapshot; every missing input resolves to nothing, and `TenantInfo.Modules` lost its default so the compiler names every construction site. Closes three fail-open links and makes the platform area's `TenantId` exemption earned rather than declared | Accepted and implemented (C1) |
| [0054](0054-limit-semantics-and-catalogue.md) | An **absent** limit means uncapped, not zero — deliberately asymmetric with the entitlement gate, which fails closed: an entitlement grants a capability, a limit only narrows one already granted, and reading a missing number as a prohibition would have stopped every existing store the day C2 shipped. And `LimitNames` is a closed catalogue, because once enforcement exists an uncountable limit name is a cap the merchant is told they have and does not. What is counted is what the merchant can empty — neither products nor staff have a hard delete, so counting the archived and the disabled would have made every limit a one-way ratchet | Accepted and implemented (C2) |
| [0055](0055-tax-as-a-configurable-capability.md) | Tax is a **configurable, jurisdiction-aware capability**, never a rule in code: a platform-maintained jurisdiction profile, versioned and frozen on publish, that any store may select; inclusive/exclusive is a property of the version rather than of the platform; an immutable snapshot on every order and invoice, so history is exact; and **tax is collected only under a version a named professional marked verified** — engineering never sets that state, so a researched value cannot reach a shopper. Adds a fifteenth module, `Tax` | **Partly implemented (2026-09-22)**: the profile, its versions, the verification workflow and a store's selection exist; the calculator and its pricing/order wiring are the next slice and must ship together. Records the owner's answer to `P-06`, which reframed the question |

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
| [0015](0015-testing-strategy.md) | Four .NET suites plus Vitest; a real SQL Server through Testcontainers; architecture rules as tests | Accurate; its "CI" revisit is answered — the suites run in `.github/workflows/ci.yml` |

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
| [0025](0025-catalog-model.md) sellable unit | [0039](0039-product-variants-order-identity.md) | A product may have more than one variant (options built in [0040](0040-product-option-model.md)); variants carry an active flag and are never deleted; order lines, pricing lines and stock administration name the variant |
| [0021](0021-transaction-boundaries.md) residual risks | [0026](0026-inventory-reservations.md), [0034](0034-notifications-outbox.md) | The abandoned-checkout sweeper and the outbox closed both risks it listed |
| [0031](0031-payments-and-refunds.md) confirmation path | [0036](0036-payment-intent-state-machine.md) | A non-succeeded confirmation no longer cancels the order unconditionally: it reads the intent's state, and a capture against a closed order is recorded instead of ignored |
| [0035](0035-white-label-runtime.md) deferring D-19 with a trigger | [0037](0037-frontend-server-state-and-types.md) | The trigger fired without producing a decision, so D-19 was split and taken: a query library is the target for server state with a named adoption point, and TypeScript's trigger became "a CI pipeline exists" rather than a date that had already passed |
| [0037](0037-frontend-server-state-and-types.md) naming two adoption points | [0038](0038-query-layer-adopted-and-type-checking.md) | Both points arrived in Phase 16 — six storefront screens rebuilt, and a CI pipeline in place — so the library was installed and migrated, and the TypeScript question was answered with checked JSDoc rather than deferred a third time |

## 4. Statements inside ADRs that no longer match the code

The decisions stand; these *descriptions* have drifted. Living documents are authoritative — this table exists so a reader of an old ADR is not misled.

| ADR | What it says | What is true today |
|---|---|---|
| 0031 | A refund retry "sends the same key, so money can't go back twice" | True only inside the provider's idempotency window — Stripe keeps keys for **24 hours**, and `RetryRefundAsync` places no age limit on retrying a `Pending` refund. Measured in M6; recorded as TD-51 with an ADR required to close it, since the fix is a payment-behaviour change |
| 0031 | Later calls for an intent go through "the account recorded on its payment" | The payment records the account **kind** (`stripe:store`), not its identity, so a refund resolves the store's *current* account. Replacing a store's keys between charge and refund sends the refund to an account that never took the money. Corrected in `Payment.cs`, `PaymentGatewayRouter.cs` and `Security.md` during M6; recorded as TD-50 |
| 0002 | Modules are namespaces in every layer (`Souq.Domain.<Module>`, `Souq.Infrastructure.*.<Module>`) | Only the Application layer has per-module folders, named after features and mapped by `ModuleMap`; the Domain is organized by kind (only Identity and Platform per module), Infrastructure by technical concern ([DependencyRules.md §5](../02-ARCHITECTURE/DependencyRules.md)) |
| 0003 | All ports live in `Application/Common/Interfaces`; a `FakePaymentService` stand-in | Ports are spread over `Common/Interfaces`, `Common/Notifications`, `Common/Security`, `Common/Tenancy`; the clock is .NET's `TimeProvider`; the payment stand-in is `FakeGateway` |
| 0004 | Modules communicate only through contracts | True in the Application layer and tested there; several modules still use another module's *domain* repositories directly ([TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md)) |
| 0005 | An `ITenantDatabaseResolver` seam keeps a per-store database possible | No such interface exists; the seam is the tenant directory, where a per-store connection would be injected |
| 0009 | Value objects `Money`, `Address`, `Slug`, `Email`, `Sku`; a pricing *domain* service | Value objects are `Money`, `PostalAddress`, `CatalogText`, `OrderActor`; slugs use the `CatalogSlug` helper; pricing is `PricingService` behind `IPricing` in the Application layer |
| 0011, 0002, 0015, 0020 | "A CI check", "will run in CI", CI in Phase 23 | CI exists ahead of Phase 23: `.github/workflows/ci.yml` runs the build and fast suites, the frontend tests, type check and build, the integration suite against SQL Server, and a dependency and secret scan, so the white-label and architecture rules run on every push to `main` or a phase branch and on pull requests. Continuous delivery is still Phase 23 |
| 0011 | A tenant admin controls templates | There are no store-editable templates; ADR-0034 rejected them. A store picks typography and theme presets |
| 0013, 0017 | The `StockChanged` 409 code | It no longer exists; insufficient stock is `422 InsufficientStock` |
| 0014 | "The default currency is still JOD"; a short zero-decimal list | `Money` has required an explicit currency since Phase 2; `CurrencyInfo` lists many more zero-decimal currencies and two four-decimal ones |
| 0019 | `RolePermissions`: `Admin` → all | There is no `Admin` role; the map covers PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff and Customer |
| 0020 | Reset links logged in Development *and* Testing | Development only |
| 0021 | Multi-save atomicity "through the EF execution strategy" | `IUnitOfWork.InTransactionAsync` over an explicit transaction; no execution strategy, and retry-on-failure is off |
| 0026 | Low-stock alert *emails* arrive in Phase 14 | Phase 14 shipped the in-app `stock.low` notification only |
| 0027 | The order has no billing address yet | It has had one since Phase 9 |
| 0028 | Shipping and tax stay at zero | Shipping is charged since Phase 12; only tax is still zero (open decision P-06) |
| 0033 | Phase 14 will notify staff of pending reviews and customers of decisions | Not built; only order status, new order and low stock exist. **Re-confirmed in M14** against the code and against the live stack (all three kinds observed), and the note is now test-backed: `NotificationDocumentationTests` fails if a fourth in-app kind or a seventh outbox message type appears without the module document being updated, so this row cannot go stale unnoticed |
| 0034 | One email provider plus a log fallback | The chain is Resend → Brevo → Gmail SMTP by which key is present; the startup rule is unchanged. **Re-confirmed in M14** on the running stack: with no provider key the chain ends at `ConsoleEmailService`, which logs the attempt with the recipient redacted (`m***@example.test`) — ADR-0020's rule holding in a real process, not inferred |
| 0035 | D-19 trigger: "the start of Phase 16" | Taken in Phase 17 by [0037](0037-frontend-server-state-and-types.md): the query library is decided and its adoption point named, and the TypeScript trigger is re-worded to a condition that can actually fire (a CI pipeline exists). Note that the branch `phase/16-engineering-knowledge-and-handoff` is this documentation pass, not the roadmap's Phase 16 (Storefront) |
| 0028 | Basket concurrency: "the last write wins (no `rowversion`). Unique owner indexes stop duplicate baskets", with optimistic concurrency rejected because "a 409 on a cart edit is noise" | Both halves are true and the decision stands, but together they read as "a basket cannot produce a 409", and it could. The resolver **writes** on a read: it merges the guest basket and deletes it, and it creates the customer basket if there is none. So two concurrent requests could race on the delete, and the unique owner index stops a duplicate basket by *refusing* the second insert — each surfacing to the shopper as a `409`. Measured and fixed as F-28 in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) (2026-09-20): the guards are unchanged and `BasketWriter` retries on committed state. Within one basket the last write still wins, exactly as decided |
| 0045 | "`TenantDomains` records each host, a domain is verified before it is trusted" | Only the first half is true. `TenantDomain.VerifiedAt` is stamped by a manual platform action and is read by **nothing** that makes a trust decision: `TenantDirectory.FindByHostAsync` does not consult it, so a host serves the storefront the moment it is added. The only readers are the platform read model and the readiness badge on the store page. Found during the commercial-readiness audit (2026-09-20); the gap itself was already recorded as F-6 in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) and as "`VerifiedAt` is decorative" in the [Platform module document](../04-MODULES/Platform/README.md) — it was this ADR's sentence that overstated it |

## 5. Numbering and lifecycle

- ADRs are numbered sequentially and never renumbered. The next one is **0056** (0047–0052 are the commercial-platform set, written 2026-09-20; 0053 was added by C1 and 0054 by C2 as each implemented them; 0055 records the owner's `P-06` answer of 2026-09-21; before them this line said 0047, and before 2026-09-20 it still said 0043 while 0043–0046 already existed — a record written from a stale line collides silently, so update it in the same commit that adds a record).
- A superseded ADR keeps its text; its `Status` line says what replaced it, and the replacement links back through `Related ADRs`.
- Rejected proposals are worth an ADR too: "we considered X and chose not to" saves the next person the same investigation ([ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) collects the big ones).
