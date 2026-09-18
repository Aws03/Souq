# Traceability: how do we know this works?

> **The question this page answers:** for each capability the product promises, where is it implemented, and which tests prove it still works?
> **Two companions:** [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) traces individual *rules* to their enforcement and tests; this page works at the level of *capabilities*. [TestInventory.md](TestInventory.md) lists every test file, generated from the source.
> **Honesty rule:** a gap recorded here is worth more than a claim of coverage. Nothing below says "covered" unless a named test proves it.
> **Last verified against the code:** 2026-09-18 (M19). The page had carried no such line, and the cost was visible: five gaps recorded here had been closed by M9–M16 and were still written as open, while five whole capability areas shipped since M8 had no row at all.

## How to read the table

- **Implementation** names where the decision is made — usually a handler and the aggregate behind it.
- **Tests** names the **classes** that fail if the capability breaks — not the files that hold them, which is a distinction this page used to blur (`ProductHandlersTests.cs` holds four classes, none of them named that).
  Suites: D = `Souq.Domain.Tests`, A = `Souq.Application.Tests`, I = `Souq.IntegrationTests`, Ar = `Souq.ArchitectureTests`, F = frontend Vitest, E2E = Playwright journeys.
- **Gaps** is what is *not* proven.

## 1. Multi-tenancy and the platform

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| A request is bound to exactly one store, from its host | `TenantResolutionMiddleware`, `TenantDirectory` | I: `TenantResolutionMiddlewareTests`, `TenantResolutionTests` · Ar: `TenancyRuleTests` | — |
| One store can never read or write another's data | `AppDbContext` query filter + `TenantWriteGuardInterceptor`, composite keys | I: `TenantIsolationTests` · Ar: `TenancyRuleTests` | Cross-module *domain* access is counted, not prevented ([ModuleDomainDependencies.md](../02-ARCHITECTURE/ModuleDomainDependencies.md)) |
| Another owner's id answers 404, not 403 | Ownership checks in the use cases (`ICurrentUser.CanAccessOwnedBy`) | I: `AuthorizationMatrixTests`, `AuthorizationBoundaryTests`, `TenantIsolationTests` | — |
| A store's status gates its endpoints | `TenantAvailabilityMiddleware` | I: `TenantResolutionTests`, `PlatformAdministrationTests` | A closed store still serves its storefront configuration and the four session endpoints, so it can render its own "unavailable" screen and its admins can sign in; everything else is 503. How much administration a *suspended* store should retain is an open product decision |
| Optional modules can be turned off per store | `RequiresModuleAttribute`, plus checks inside the coupon use cases | I: `PlatformAdministrationTests`, `ReviewModerationTests`, `WishlistTests` | Reviews and wishlist are gated only at the endpoint, not inside their use cases |
| Platform actions are audited | `AuditBehavior` over `IAuditable` | Ar: every platform request must be auditable · I: `PlatformAdministrationTests` | Store-side actions (order status, coupons, shipping) are **not** audited |
| Store settings drive the storefront | `UpdateStoreSettingsCommand`, `GetStorefrontConfigQuery`, the directory cache | I: `StoreAdministrationTests`, `PlatformAdministrationTests` · F: `tenantModel.test.js` | Cache invalidation is per process (R-23) |

## 2. Identity and access

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| Sign in, with lockout | `LoginCommand`, `User` | D: `UserTests` · A: `LoginHandlerTests`, `PasswordRulesTests` · I: `AuthSessionTests` | Lockout is evaluated before the password check, which reveals that an account exists |
| Sessions rotate, reuse is detected | `RefreshSessionCommand`, `RefreshToken` | D: `RefreshTokenTests` · I: `AuthSessionTests` | — |
| A token is valid only on its own store's host | `AccessTokenValidation` | I: `TenantIsolationTests`, `AuthSessionTests` | — |
| Permissions gate every endpoint | `HasPermissionAttribute`, `RolePermissions` | Ar: `EndpointRuleTests` (explicit access, platform permissions) · A: `RolePermissionsTests` · I: `AuthorizationMatrixTests` | **One** permission is granted and used by no endpoint (`platform.settings.manage`); `store.reports.view` gained `StoreReportsController` |
| Reset, verification and invitation tokens are single-use, hashed, expiring | `User`, the identity email handlers | D: `UserTests` · A: `IdentityEmailHandlersTests` · I: `AuthSessionTests`, `NotificationTests` | — |
| A signed-in person changes their own password, and every other session falls | `ChangePasswordCommand`, `User.SecurityStamp` | A: `ChangePasswordHandlerTests`, `AccountLifecycleTests`, `PasswordRulesTests` · I: `AuthSessionTests` · F: `ChangePassword.test.jsx` · E2E: `account-password.spec.js` | — |
| Staff and platform accounts are invited, not created with passwords | `AccountInvitations` | D: `InvitationAndAuditTests` · I: `StoreAdministrationTests`, `PlatformAdministrationTests` | — |

## 3. Catalog and inventory

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| Products exist per store with per-language text and a default variant | `Product`, `CreateProductCommand` | D: `ProductTests` · A: `CreateProductHandlerTests`, `UpdateProductHandlerTests`, `ProductLifecycleHandlerTests` · I: `CatalogTests` | — |
| Slugs and SKUs are unique per store | `Product`, unique indexes | I: `CatalogTests` | — |
| Only published products in an active category are sellable | `Product.IsSellable` (active product **and** active category) in the pricing pipeline, the basket and the wishlist | D: `ProductTests`; A: `PricingServiceTests`, `BasketHandlersTests`, `WishlistHandlersTests`; I: `CatalogTests` | Closed in Phase 17 (was R-07): sellability is one Domain rule, so visibility and purchasability can no longer disagree (BR-CAT-17) |
| Stock never goes negative; every change is ledgered | `InventoryItem` | D: `InventoryItemTests` · A: `InventoryCommandsTests` · I: `InventoryAndOrderTests` · Arch: `ModuleAndContractRuleTests` | M7 added the race that actually happens — **commit against release on the same reservation** (payment confirming as the expiry sweep releases) — and ratcheted the ledger half: Σ movements = on hand was audited across every write path and found sound, then a rule was added that fails the build if `StockMovement` is constructed inside Infrastructure, which `InternalsVisibleTo` permits and which would break reconciliation with no exception raised |
| An order line records the exact variant bought, with SKU and label snapshots, and never merges two variants; basket, pricing and checkout carry the variant; a foreign, deactivated or missing variant is refused; stock is administered per variant | `Order.AddItem`, `Product.FindVariant`/`CanSell`/`ImplicitVariant`, `PricingService`, `BasketLines`, `StockTarget`, migration `OrderLinesRecordVariant` | D: `OrderTests`, `ProductVariantTests`, `BasketTests` · A: `PricingServiceTests`, `BasketHandlersTests`, `BasketCheckoutTests`, `CreateOrderHandlerTests`, `InventoryCommandsTests` · I: `ProductVariantTests`, `TenantIsolationTests`, `MigrationRehearsalTests` | — (V2 shipped: `CreateProductVariantsCommand` is exposed as `POST /api/admin/products/{id}/variants` and driven through the real API by `ProductOptionAdminTests`) |
| Merchants define up to 3 options (20 values each, names per language, unique, no control characters) and manage variants as unique, complete combinations (up to 100, inactive counted); a used value is never removed; the default is movable and always active; product-level pricing is refused on a product with options; structural edits are concurrency-guarded; the label is snapshotted on order lines, shown per variant in inventory and in low-stock notifications; the storefront shows every product with a purchasable variant (V3 shipped; `CatalogQueries.VisibleProducts()` carries no variant-count restriction) | `Product.SetOptions`/`AddVariant`/`UpdateVariant`/`SetDefaultVariant`, `ProductVariantCommands.cs`, `IProductRepository.GuardConcurrentEdit`, `CombinationKey` index, `CatalogQueries.VisibleProducts` | D: `ProductOptionTests`, `ProductVariantTests` · A: `ProductVariantHandlersTests`, `OrderNotificationHandlersTests` · I: `ProductOptionAdminTests`, `TenantIsolationTests`, `MigrationRehearsalTests` · FE: `variantModel.test.js`, `ProductVariants.test.jsx`, `Inventory.test.jsx` · E2E: `product-variants.spec.js`, `admin-inventory.spec.js` | ADR-0040; BR-CAT-19 to BR-CAT-24, BR-INV-13, BR-NTF-12. The inventory screen had no test of its own until M7 |
| The storefront shows a product's options and active variants with their availability; the shopper chooses explicitly, sold-out and impossible values are disabled and no impossible combination is reachable; lists, filters, sorting and structured data price from the cheapest **purchasable** variant ("From" when they differ); a deactivated variant is hidden and a fully sold-out product stays listed; the label is live per language in the basket and frozen on the order and its email | `CatalogQueries` (purchasable price, options and variants), `WishlistQueries`, `ProductDto`, `variantSelection.js`, `VariantPicker.jsx`, `structuredData.js`, `PricedLine.VariantLabels`, `EmailLine.Variant` | FE: `variantSelection.test.js`, `ProductDetail.test.jsx`, `ProductCard.test.jsx`, `basketModel.test.js`, `structuredData.test.js` · I: `StorefrontVariantTests`, `ProductOptionAdminTests` · E2E: `storefront-variants.spec.js` | ADR-0041; BR-CAT-24 to BR-CAT-27 |
| Checkout reserves, payment commits, cancellation releases | `InventoryReservations`, `OrderPaymentConfirmation` | A: `InventoryReservationsTests`, `ConfirmOrderPaymentHandlerTests` · I: `InventoryAndOrderTests` | — |
| Abandoned checkouts are settled | `ExpireStaleCheckoutsCommand` + the hosted sweep | A: `ExpireStaleCheckoutsHandlerTests` · I: `InventoryAndOrderTests`, `BackgroundSweepScopeTests` | The sweep covers active **and suspended** stores since M5 (R-24 closed); provisioning and archived are excluded |
| Low stock raises an event | `InventoryItem`, `StockBecameLowHandler` | D: `DomainEventTests` · I: `NotificationTests` | No low-stock email exists (the ADR expected one) |

## 3a. Search, and what it is asked

Added in M19: catalog search shipped in M3 and its analytics in M13, and neither had a row here — roughly a
sixth of the integration suite was invisible to this page.

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| Arabic text matches as people type it: diacritics, tatweel, alef/ة/ى folding, Arabic-Indic digits and punctuation are normalized **into a stored, indexed column** | `SearchText.Normalize`, `Product.SearchText`, `CatalogQueries` | D: `SearchTextTests` · I: `CatalogSearchTests`, `CatalogSearchProjectionTests` | — |
| Every query word must match, in a name, a description or a category name; `Relevance` ranks in SQL | `CatalogQueries`, `ProductSortBy.Relevance` | I: `CatalogSearchTests` | — |
| A typo recovers from the store's own vocabulary and **says which word it searched**; the shopper can refuse (`exact=true`) | `SearchDistance`, the suggestion pipeline | D: `SearchDistanceTests` · I: `CatalogSearchTests` · F: `SearchBar.test.jsx` · E2E: `search.spec.js` | — |
| A merchant teaches the vocabulary its customers use | `SearchSynonym` | D: `SearchSynonymTests` · A: `SearchSynonymHandlersTests` · I: `CatalogSearchTests` | — |
| Searches are logged with **no personal data by construction**, buffered, and purged after 90 days | `SearchQueryLog`, `SearchLogBuffer`, `SearchLogWriterService`, `PurgeSearchLogCommand` | A: `PurgeSearchLogHandlerTests` · I: `SearchAnalyticsTests` · Ar: `SearchRuntimeRuleTests`, `TenancyRuleTests` · E2E: `admin-search-insights.spec.js` | **`SearchQueryLog` has no Domain test class of its own** — its no-personal-data-by-construction rule is proven only through the integration suite. `SearchLogWriterService`'s own loop is exercised only through the buffer |

## 3b. Reporting and dashboards

Added in M19. M12 was entirely about merchant-facing numbers — and found four of them misleading — with no row here.

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| The merchant dashboard counts only what it says it counts (a pending order is not revenue), in the store's currency and its minor units | `StoreReportQueries`, `ReportWindow` | A: `StoreDashboardTests` · I: `StoreDashboardTests` · F: `Dashboard.test.jsx`, `BusinessOverview.test.jsx` | Profit and margin are deliberately not shown — the system does not know cost |
| A store sees only its own numbers | the query filter, `StoreReportQueries` | I: `StoreDashboardTests`, `TenantIsolationTests` | — |
| Platform-wide statistics are the owner's, not a store's | `PlatformStats` | I: `PlatformAdministrationTests` · F: `PlatformOverview.test.jsx` | — |
| Read paths stay inside a query budget as data grows | `ReadPathQueryBudgetTests` | I: `ReadPathQueryBudgetTests`, `BestSellingPerformanceTests` | Measured on an emulated, memory-capped stack: compare within a run, not against absolute numbers |

## 4. Shopping, orders and money

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| A guest basket survives and merges at sign-in | `BasketResolver` | A: `BasketHandlersTests` · I: `BasketTests` | — |
| One pricing pipeline serves the basket and checkout | `PricingService` | A: `PricingServiceTests`, `CreateOrderHandlerTests` · I: `BasketTests` | Tax is a fixed zero (P-06) |
| Coupon limits hold under concurrency | `Coupon`, `CouponRedemptions` | D: `CouponTests`, `CouponRuleMatrixTests` · A: `CouponRedemptionsTests` · I: `CouponRedemptionTests` | One evaluator since M8: the second, weaker path at `/api/coupons/apply` was deleted (TD-06 closed), so no coupon-adjacent endpoint can accept a code checkout would refuse |
| Shipping is chosen, priced and snapshotted | `StoreShippingRates`, `Order.ApplyShipping` | D: `ShippingMethodTests`, `OrderShippingTests` · A: `StoreShippingRatesTests` · I: `ShippingTests` | — |
| Orders are numbered per store and immutable after placement | `Order`, `OrderNumbers` | D: `OrderTests`, `OrderLifecycleTests` · I: `OrderLifecycleTests` | — |
| Only allowed status transitions happen, with the actor recorded | `OrderTransitions` | D: `OrderLifecycleTests` · A: `UpdateOrderStatusHandlerTests` · I: `OrderLifecycleTests` | Order-status changes are not audited |
| Payment confirmation is idempotent from both doors, and reads the intent's state rather than a boolean | `OrderPaymentConfirmation`, `PaymentIntentState` | A: `ConfirmOrderPaymentHandlerTests`, `ProcessPaymentWebhookHandlerTests` · I: `PaymentsAndRefundsTests` | The mapping of real Stripe statuses onto the four states is unverified against a live account ([ADR-0036](../11-ADR/0036-payment-intent-state-machine.md)) |
| Refunds cannot exceed the capture and are retry-safe | `Payment`, `OrderPayments` | D: `PaymentTests` · A: `OrderPaymentsTests` · I: `PaymentsAndRefundsTests` | The Stripe adapter itself has no test (TD-33) |
| Money keeps its currency and minor units | `Money`, `CurrencyInfo`, `StripeAmountConverter` | D: `MoneyTests` · I: `StripeAmountConverterTests`, `InventoryAndOrderTests` | **JOD minor units at Stripe are unverified against a real account (P-05, R-01)** |

## 5. Customers, reviews, notifications

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| A customer profile is separate from the login account | `Customer`, `RegisterHandler` | D: `CustomerProfileTests` · I: `CustomerAccountTests` | — (**TD-03 closed in M9**: the Identity↔Customers cycle is gone, through `IAccountProfiles`/`IAccountLifecycle`) |
| Address book with defaults | `Customer`, the account use cases | D: `CustomerProfileTests` · I: `CustomerAccountTests` | `Customer` has no concurrency token |
| Blocked customers cannot buy | `Customer.IsBlocked` checks | A: `CreateOrderHandlerTests`, `CreateOrderCustomerRulesTests` · I: `CustomerAccountTests` | A blocked customer can still fill a basket and a wishlist |
| Export and erasure keep orders and anonymize the person | `CustomerErasure` | A: `CustomerAccountHandlersTests`, `AccountLifecycleTests` · I: `CustomerAccountTests` | In-app notifications survive erasure |
| Reviews require a verified purchase, one per product | `Review`, `CreateReviewHandler` | D: `ReviewTests` · A: `CreateReviewHandlerTests` · I: `ReviewModerationTests` | — |
| Moderation follows the store's policy and is audited | `ReviewModeration`, `Tenant.ReviewsAutoApprove` | A: `ReviewModerationHandlersTests` · I: `ReviewModerationTests` | No moderation notifications (deferred) |
| Nothing is lost or sent twice after a commit | the outbox | A: `OutboxPolicyTests` · I: `NotificationTests` · Ar: `NotificationDocumentationTests` | **TD-34 closed in M14**: purge, both halves of the lease, and two dispatchers racing are each tested, and each was proven to fail against deliberately broken code. `OutboxDispatcherService` itself is still untested |
| No token or personal data reaches the logs | `LogRedaction`, `RequestLoggingMiddleware`, the handlers | I: `NotificationTests`, `ObservabilityTests` | The API redacts sensitive route values, and the shipped nginx logs the `souq_safe` format without query strings (G-03 closed). A proxy in front of the stack keeps its own log |

## 6. Cross-cutting guarantees

| Guarantee | Enforced by | Tests |
|---|---|---|
| The dependency rule and thin controllers | project references + IL analysis | Ar: `DependencyRuleTests` |
| Module contracts and no cycles | namespace analysis | Ar: `ModuleAndContractRuleTests` |
| No client-chosen tenant, no entity in a contract, no `IQueryable` leak | reflection over requests | Ar: `ModuleAndContractRuleTests` |
| Time is injected everywhere | IL scan | Ar: `ClockRuleTests` |
| Every endpoint declares its access | reflection over controllers | Ar: `EndpointRuleTests` |
| No brand or currency literal in product code | source scan | Ar: `WhiteLabelSourceTests` · F: `whiteLabel.test.js` |
| The documentation matches the code | link, path, symbol and ADR checks | Ar: `DocumentationTests` |
| The inventories match the code | generation and comparison | Ar: `GeneratedDocsTests` |
| Migrations preserve existing data | a rehearsal over pre-run data | I: `MigrationRehearsalTests` |
| Startup refuses an unsafe configuration | fail-fast validation | I: `ConfigurationTests`, `StartupAndSecurityTests` |

## 6a. Operability: does it run, and can it be undone?

Added in M19 — M17 and M18 are absent from this page otherwise.

| Guarantee | Enforced by | Tests |
|---|---|---|
| Four counters emit, tagged by cause and without high-cardinality tags | `SouqMetrics` | A: `SouqMetricsTests` |
| Liveness and readiness answer different questions, and readiness refuses a stale schema | `DatabaseHealthCheck` | I: `HealthCheckTests` |
| The backup verifier fails **closed** — a stale, future-dated or unreadable backup is a failure, not a note | `scripts/backup-verify.sh`, `lib.sh` `utc_epoch` | Ar: `BackupVerificationScriptTests` |
| Operational scripts run on the system shell, not only a modern bash | source scan | Ar: `OperationalScriptTests` |
| The shipped compose stack sets every setting the API refuses to start without | derived from `Program.cs`'s own guards | Ar: `ConfigurationSourceTests` |
| Migrations can be stepped back, and a data-reshaping `Down()` is lossy on purpose | a rehearsal over pre-run data | I: `MigrationRehearsalTests`, `MigrationRollbackTests` · Ar: `MigrationSafetyTests` |
| Every command has a validator of the right shape | reflection over the inheritance chain | Ar: `ValidationRuleTests` |
| Security headers, CSP, cookies and uploads behave as documented | real requests | I: `SecurityHeadersTests`, `ContentSecurityPolicyTests`, `CookieSecurityTests`, `UploadSecurityTests` · E2E: `csp.spec.js` |

## 7. Where traceability stops

Be aware of these when you plan work:

1. **CI does not run at all right now, and would not block merges even then.** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs the .NET suites and the frontend lint, type-check, tests and build on every push; until branch protection is switched on, a red run can still be merged — and since before M13 GitHub Actions has refused to start any job for billing reasons, so nothing reports (TD-31, [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md)). The Playwright journeys are not in CI at all. A **release pipeline** also exists now — [`.github/workflows/release.yml`](../../.github/workflows/release.yml), whose gate job runs all four .NET suites and a clean-tree check before any image is published — and it has never run either, for the same billing reason.
2. **Frontend component coverage is partial**: the guards, the error boundary, the account shell, the order screens and checkout's money barriers are tested; form-level interaction and most admin screens are verified only by the Playwright journeys or by hand (TD-32).
3. **External adapters are untested**: Stripe and the three email providers are exercised only through fakes (TD-33).
4. **Some rules are untested**, listed in the gaps section of [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) — notably several length and size caps on merchant-editable text and uploads. Two items this page used to name here are **done**: the sweeps for non-active stores closed in M5 (`BackgroundSweepScopeTests`) and the password policy in M9 (`PasswordRulesTests`, which pins the advertised limits). A **coverage audit in M19** read all 229 rules against the test bodies rather than the documents' own Tests columns, and its finding is the honest headline: most rules are named by a test, but many name a test that asserts *part* of what the rule says — see [TestingStrategy.md](TestingStrategy.md) §"What the coverage audit found".
5. **Performance is partly measured**: M16 added a query budget over the read paths (`ReadPathQueryBudgetTests`), a best-selling budget, and a first-load bundle budget enforced in [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) through a real browser. `scripts/load-test.py` exists and is run by hand; there is **no load test in any pipeline**, and every absolute figure comes from an emulated, memory-capped stack.
6. **Two capabilities the documentation once promised do not exist**: low-stock emails and moderation notifications. They are marked DEFERRED in their module documents rather than quietly dropped.
