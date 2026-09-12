# Release readiness

> **What this page is:** every known blocker between Souq today and a first paying customer, triaged by severity, with the evidence that it is real, what closing it takes, and what proves it closed. It is the page to read before saying "we can go live".
> **How it differs from its neighbours:** [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) says *how this system can hurt someone*; [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) says *what we chose to postpone*; this page says **what stops a release**, and tracks each item to closed.
> **Checklist for the release itself:** [ProductionReleaseChecklist.md](ProductionReleaseChecklist.md).

## How this page is built

Every item below was **re-verified against the code during Phase 17**, not copied forward. Where the earlier register's description turned out to be incomplete or wrong, this page says so. Three items were found in Phase 17 and have no entry in the older registers.

**Severity**

| | Meaning |
|---|---|
| **P0** | Must be closed before any real customer or real money. Money loss, data loss, a broken deployment, or a legal bar |
| **P1** | Should be closed before the first commercial customer. Correctness or security weakness with a workaround, or an operational gap |
| **P2** | Acceptable to ship with a written limitation |
| **P3** | Future improvement; no release depends on it |

**Status**

`FIXED (Phase 17)` · `OPEN` · `BLOCKED — owner decision` · `BLOCKED — external verification` · `ACCEPTED — documented limitation`

**Evidence** is either *verified in code* (someone read the path and can name it), *reproduced* (a test or a run demonstrates it), or *inherited* (carried from an earlier pass and re-read, but the failure has not been reproduced).

---

## P0 — must be closed before real customers or real money

| # | Blocker | Evidence | Remediation | Tests | Status |
|---|---|---|---|---|---|
| R-02 | **A declined card could capture money against a cancelled order.** A `payment_intent.payment_failed` webhook cancelled the order while the intent stayed alive at `requires_payment_method`; the shopper retried on the same client secret, the money was captured, and the later success found a non-`Pending` order and returned an idempotent success. The `Payment` stayed `Failed`, which no refund path accepts | Verified in code and reproduced: the confirmation path asked the gateway for a boolean and treated every non-success as final, unlike the cancellation paths which already asked first | `PaymentConfirmationResult` now carries the intent's state; only a `Cancelled` intent cancels the order, a `Retryable` one leaves it `Pending` for the shopper to retry, and a capture against a closed order is recorded by `Payment.MarkCapturedAfterClose` so the normal refund path accepts it ([ADR-0036](../11-ADR/0036-payment-intent-state-machine.md)) | `PaymentTests`, `ConfirmOrderPaymentHandlerTests` (5 cases), `PaymentsAndRefundsTests` | **FIXED (Phase 17)** |
| R-13 | **Every uploaded image and store logo 404s through the shipped stack.** `/uploads/` was proxied with no `Host` override, so nginx sent `Host: api:8080`; `TenantResolutionMiddleware` runs on `/uploads`, resolves the store from the host, finds none, and answers `404 StoreNotFound` | Verified in code: the `/api/` location sets `proxy_set_header Host $http_host`, the `/uploads/` location set nothing | `frontend/nginx.conf` now forwards `Host`, `X-Forwarded-For` and `X-Forwarded-Proto` on `/uploads/` exactly as on `/api/` | *Needs a running stack to prove; no automated test covers the proxy* | **FIXED (Phase 17)** |
| R-17 | **A fresh production database is seeded with demo data.** `DbSeeder` applied the demo store's branding, three categories, eight products and their stock unconditionally — the environment was never consulted | Verified in code: `SeedCatalogAsync` and `ApplyDefaultStoreLookAsync` were gated only on "has no categories yet" and "settings never customised" | `DbSeeder.ShouldSeedDemoData` gates both on the environment, overridable in either direction by `Seed:DemoData`. Production initialization now keeps only what is configured deliberately (platform owner, first admin, host binding), and the seeder logs that demo data was skipped | `ConfigurationTests` (the decision table, including the explicit override in both directions) | **FIXED (Phase 17)** |
| R-06 | **The order confirmation email prints the wrong total, or never arrives.** `OrderEmailHandler` loads the order through `GetByIdAsync`, which is `FindAsync` — the root alone, no lines. `Order.TotalAmount` then sums an empty collection, so the email shows shipping only; with a coupon, `Subtotal.Subtract(DiscountAmount)` goes negative and `Money` throws, so the message retries until the outbox marks it dead | Verified in code: `OrderRepository` overrides only `GetWithItemsAsync`; nothing configures an auto-include | Use the frozen `PlacedTotal`, and itemise the email from the order's lines | `OrderNotificationHandlersTests`, `NotificationTests` | **OPEN — in progress (Phase 17)** |
| R-12 | **The application connects to SQL Server as `sa`.** A compromise or an injected query has full server privileges | Verified in `docker-compose.yml`: `User Id=sa` | Create a least-privilege SQL login owning only `SouqDb`, and change `ConnectionStrings__Default`. This is a deployment action, not a code change | *None — no test can assert the deployment's credentials* | **OPEN — deployment action** |
| R-16 | **No TLS, HSTS or security headers in the repository's own deployment**, and nginx overwrites `X-Forwarded-Proto` with its own (plain http) scheme, so links generated for emails can come out `http://` | Verified in code: nginx listens on port 80 only; the API calls neither HTTPS redirection nor HSTS | Terminate TLS in front, pass the outer scheme through instead of overwriting it, and add the header set before any public launch | `StartupAndSecurityTests` covers the API's own headers only | **OPEN — deployment action** |
| R-19 | **No automated backups and no rehearsed restore.** Data loss is unbounded, and `SECRETS_KEY` is unrecoverable — without it every stored store payment key is permanently unreadable | Verified: no backup job, schedule or script exists in the repository | Define and **rehearse** backup and restore of the database, the uploads volume and the secrets together, before the first paying store | *None* | **OPEN — operational** |
| R-01 | **JOD (and every three-decimal currency) may be charged at one tenth of the price.** `StripeAmountConverter` multiplies by 100 for everything Stripe does not list as zero-decimal. If the real connected account treats JOD as three-decimal, the correct multiplier is 1000 | Verified in code; the *behaviour of the real account* cannot be verified from this repository | Create a test intent for a three-decimal amount on the real account and compare the dashboard. Applies to BHD, IQD, KWD, LYD, OMR, TND as well — the original risk named only JOD | `StripeAmountConverterTests` pins the current arithmetic, not its correctness | **BLOCKED — external verification (P-05)** |
| R-25 | **No tax model exists.** The pricing pipeline's tax stage is a fixed zero | Verified in code: `PricingService` sets tax to zero explicitly | Decide inclusive/exclusive pricing, per-store rates and invoice requirements. A legal bar in most jurisdictions, not an engineering preference | `PricingServiceTests` asserts the explicit zero | **BLOCKED — owner decision (P-06)** |

## P1 — should be closed before the first commercial customer

| # | Blocker | Evidence | Remediation | Tests | Status |
|---|---|---|---|---|---|
| R-08 | **A suspended store answers 503 to everything**, including its own storefront configuration and sign-in, so it cannot show a branded "closed" page and its administrators cannot sign in to fix anything. `AvailableWhenStoreClosedAttribute` exists and is honoured — and was applied to **no endpoint** | Verified in code: a repository-wide search found the attribute only in its own definition, in `TenantAvailabilityMiddleware.IsOpen`, and in the documentation generator | The attribute is applied to exactly five endpoints — the storefront configuration, sign-in, refresh, sign-out and "who am I" — endpoint by endpoint rather than to a whole controller, so registration, password reset and every permission-protected endpoint stay closed. **Still open as a product decision:** how much administration a suspended store should retain beyond signing in (see P1 below) | `TenantResolutionTests` (catalog 503, config 200, admin signs in, admin endpoints still 503, registration 503) | **FIXED (Phase 17)** |
| R-07 | **A product in a disabled category is hidden but still purchasable.** `CatalogQueries.VisibleProducts` filters on the category, but `PricingService` marks a line sellable from `Product.IsActive` alone, and checkout trusts that flag | Verified in code across the storefront, basket and checkout paths | Define sellability in one place in the Domain and enforce it in the pricing pipeline, the basket and the wishlist | `PricingServiceTests`, `BasketHandlersTests`, `CatalogTests` | **OPEN — in progress (Phase 17)** |
| R-10 | **The public order tracking token is written to the request log.** `RequestLoggingMiddleware` logs `Request.Path`, and the token is a path segment of `/api/orders/track/{token}`; nginx's default access log records it too | Verified in code | Sensitive route parameters are redacted by **value** from the logged path, keeping the route template for aggregation. **Remaining gap (G-03):** the proxy's access log still records the SPA's token-bearing query strings for reset, verification and invitation links — a log-policy decision at the proxy | `ObservabilityTests` (the path is `/api/orders/track/***`, the template survives, the token is in no log entry) | **FIXED (Phase 17) — API side; proxy log policy still open** |
| R-22 | **`.env.example` shipped a non-empty Gmail placeholder**, which defeats the "no email provider" fail-fast: the API starts, selects Gmail, and every message dies after eight attempts | Verified in code: `AddEmail` selects Gmail on any non-empty `Gmail:AppPassword` | The placeholder is now empty, with the consequence written next to it | `NotificationTests` proves the fail-fast itself | **FIXED (Phase 17)** |
| R-03 | **Cancelling a paid order refunds it in full on `orders.manage` alone.** `TenantStaff` holds that permission and not `store.payments.manage`, so the daily operator can move money out | Verified in code: `UpdateOrderStatusHandler` calls the refund after the cancellation commits, with no payments-permission check | Either require the payments permission for a refunding transition, or audit order-status changes explicitly. Both change staff workflow, so the choice is the owner's | `UpdateOrderStatusHandlerTests`, `AuthorizationMatrixTests` | **OPEN — owner decision** |
| R-11 | **Rate limits are per process and partition on a forwarded client address.** With the API port published and the Docker bridge inside `TRUSTED_PROXY_NETWORKS`, a direct client may forge `X-Forwarded-For` | Inherited; depends on the deployment's networking and was not reproduced | Do not publish the API port when a proxy fronts the stack; trust only the proxy's own address; move limits to the edge when scaling out | `AuthSessionTests` covers the limiter, not the spoofing path | **OPEN — deployment action** |
| R-20 | **No health checks and no CI pipeline.** A broken deploy is found by a customer; a red suite can be committed | Verified: no health endpoint is mapped and no CI configuration exists | Add a health endpoint and a pipeline running the five suites. Requirements are written in [DeveloperQualityGates.md](DeveloperQualityGates.md) | — | **OPEN** |
| R-18 | **Migrations run automatically at startup in every environment**, so a bad migration is applied by the act of deploying, and two instances starting together race | Verified in code: `DbSeeder.SeedAsync` calls `MigrateAsync` | Back up first; move to a deliberate migration step before rollout | `MigrationRehearsalTests` covers the migrations, not the startup policy | **ACCEPTED for a single instance — documented** |
| R-26 | **The payment account model (D-13) is undecided**: every store connects its own Stripe account, or the platform adopts Stripe Connect. Until then the deployment account is the merchant of record by default | Verified: both mechanisms exist; the choice does not | Decide before onboarding a second paying store — it affects liability, fees and refunds | `PaymentsAndRefundsTests` covers both routing paths | **BLOCKED — owner decision (D-13)** |
| R-27 | **The repository is MIT-licensed with a public remote.** Anyone receiving the code may resell it | Verified: `LICENSE` is MIT | Decide the licence and repository visibility before the first sale | — | **BLOCKED — owner/legal decision (P-03)** |

## P2 — acceptable to ship with a written limitation

| # | Limitation | Evidence | Why it is acceptable today | Status |
|---|---|---|---|---|
| R-14 | Cross-module domain access is counted, not prevented (78 crossings) | Verified and **fully classified in Phase 17**: [ModuleBoundaryAudit.md](../02-ARCHITECTURE/ModuleBoundaryAudit.md) | Every crossing now has a written reason and a class; the ratchet stops new ones appearing quietly | **ACCEPTED — audited** |
| R-15 | Identity and Customers form a dependency cycle the target graph forbids | Verified: 12 crossings, 8 of them writes, in both directions | Neither module can be extracted alone, but nothing is incorrect at runtime | **ACCEPTED — documented** |
| R-04 | The store payment-account use cases live in the Platform feature folder although Payments owns the concept | Verified: they create and update the `StorePaymentAccount` aggregate from `Features/Stores` | The most sensitive code is attributed to the wrong module by the tests and docs; behaviour is correct | **ACCEPTED — documented** |
| R-09 | A store's currency locks only after a product or an order exists, so coupons and shipping methods created earlier keep the old currency | Inherited, re-read and confirmed | Affects a store that changes currency before trading — rare and recoverable | **ACCEPTED — documented** |
| R-05 | A refund does not give a coupon use back | Verified; recorded in [ADR-0031](../11-ADR/0031-payments-and-refunds.md) | A deliberate product choice, not a defect | **ACCEPTED — product decision** |
| R-21 | Refresh tokens, stock reservations, in-app notifications, audit entries and dead outbox rows are never purged | Verified: only processed outbox rows are purged | Growth is slow at current volume; personal-data retention needs a legal decision first | **ACCEPTED — documented** |
| R-24 | Per-store sweeps run only for Active stores, so a suspended store's expired checkouts never settle and its baskets are never purged | Verified in `StoreSweepService`; the outbox dispatcher is deliberately status-agnostic, which is an inconsistency nothing recorded before Phase 17 | Stock stays reserved in a store that is not selling | **ACCEPTED — documented** |
| R-23 | In-process caches (tenant directory, session stamps) with no cross-instance invalidation | Verified | Correct for one instance, which is the only supported topology today | **ACCEPTED — single instance only** |
| F-1 | `ExecuteUpdate` / `ExecuteDelete` bypass the write guard, and **no architecture rule covers them**. `OrderNumbers.NextAsync` issues an unqualified bulk update whose isolation rests entirely on the query filter | **Found in Phase 17.** Verified in code; `TenancyRuleTests` scans for `IgnoreQueryFilters` and raw SQL but not for bulk operations | Not exploitable as written — EF applies query filters to bulk operations — but the next such call has no defence and no test | **OPEN — rule to add** |
| F-3 | `Customer.UserId` and `RefreshToken.UserId` are single-column foreign keys, and the composite-FK architecture rule structurally cannot reach them because `User` is `ITenantOrPlatformOwned` | **Found in Phase 17.** Verified in the EF configurations and in the rule's own predicate | Unreachable today: every lookup is tenant-filtered and the guard stamps the tenant. The database, however, does not prevent it | **ACCEPTED — documented** |
| R-28 | The frontend stack decision (D-19) is deferred and its trigger has arrived | Verified | Evaluated in Phase 17; see the decision record | **OPEN — decision due this phase** |

## P3 — future improvement

| # | Item | Note |
|---|---|---|
| F-2 | On a platform host, `/uploads/tenants/{anyId}/…` serves any store's files: the middleware returns before the ownership check | **Found in Phase 17.** Deliberate per [ADR-0022](../11-ADR/0022-tenancy-enforcement.md); filenames are 128-bit random |
| F-4 | The unique index on reviews is `(CustomerId, ProductId)` — the only unique index on tenant-owned data that does not lead with `TenantId` | Harmless while ids are global; becomes a cross-store conflict oracle if ids ever become per-tenant |
| F-6 | A domain serves the storefront the moment it is added; `VerifiedAt` is recorded but never gates anything | Platform-only action, and a host cannot be stolen from another store |
| R-05a | Every refund is sent to the gateway with the reason `requested_by_customer`, whatever the real reason was | Cosmetic in the provider dashboard |

## Not yet assessed

These are named in the mission and not yet re-verified in code; they carry no severity until they are.

- Concurrency behaviour under simultaneous requests beyond the paths already covered by tests (inventory, coupons, refunds are covered; tenant configuration and product updates are not).
- Idempotency coverage outside payments, refunds and the outbox.
- Performance: N+1 queries and unbounded reads on the admin and reporting paths.

## How to use this page

1. **Before a release:** everything in P0 is either `FIXED` or has a deliberate, written acceptance from the owner. No exceptions — P0 is defined as "money, data, deployment or law".
2. **Before the first commercial customer:** work down P1. Items marked *owner decision* cannot be closed by engineering alone; take them to the owner as a batch.
3. **When an item closes:** move it to `FIXED` with the commit, and delete its row from [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) and its entry in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) in the same change. This page keeps the history; those two keep only what is still true.
