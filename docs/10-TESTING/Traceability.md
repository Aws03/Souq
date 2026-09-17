# Traceability: how do we know this works?

> **The question this page answers:** for each capability the product promises, where is it implemented, and which tests prove it still works?
> **Two companions:** [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) traces individual *rules* to their enforcement and tests; this page works at the level of *capabilities*. [TestInventory.md](TestInventory.md) lists every test file, generated from the source.
> **Honesty rule:** a gap recorded here is worth more than a claim of coverage. Nothing below says "covered" unless a named test proves it.

## How to read the table

- **Implementation** names where the decision is made — usually a handler and the aggregate behind it.
- **Tests** names the classes that fail if the capability breaks. Suites: D = `Souq.Domain.Tests`, A = `Souq.Application.Tests`, I = `Souq.IntegrationTests`, Ar = `Souq.ArchitectureTests`, F = frontend Vitest.
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
| Sign in, with lockout | `LoginCommand`, `User` | D: `UserTests` · A: the auth handler tests · I: `AuthSessionTests` | Lockout is evaluated before the password check, which reveals that an account exists |
| Sessions rotate, reuse is detected | `RefreshSessionCommand`, `RefreshToken` | D: `RefreshTokenTests` · I: `AuthSessionTests` | — |
| A token is valid only on its own store's host | `AccessTokenValidation` | I: `TenantIsolationTests`, `AuthSessionTests` | — |
| Permissions gate every endpoint | `HasPermissionAttribute`, `RolePermissions` | Ar: `EndpointRuleTests` (explicit access, platform permissions) · A: `RolePermissionsTests` · I: `AuthorizationMatrixTests` | Two permissions are granted but used by no endpoint |
| Reset, verification and invitation tokens are single-use, hashed, expiring | `User`, the identity email handlers | D: `UserTests` · A: `IdentityEmailHandlersTests` · I: `AuthSessionTests`, `NotificationTests` | — |
| Staff and platform accounts are invited, not created with passwords | `AccountInvitations` | D: `InvitationAndAuditTests` · I: `StoreAdministrationTests`, `PlatformAdministrationTests` | — |

## 3. Catalog and inventory

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| Products exist per store with per-language text and one default variant | `Product`, `CreateProductCommand` | D: `ProductTests` · A: `ProductHandlersTests` · I: `CatalogTests` | — |
| Slugs and SKUs are unique per store | `Product`, unique indexes | I: `CatalogTests` | — |
| Only published products in an active category are sellable | `Product.IsSellable` (active product **and** active category) in the pricing pipeline, the basket and the wishlist | D: `ProductTests`; A: `PricingServiceTests`, `BasketHandlersTests`, `WishlistHandlersTests`; I: `CatalogTests` | Closed in Phase 17 (was R-07): sellability is one Domain rule, so visibility and purchasability can no longer disagree (BR-CAT-17) |
| Stock never goes negative; every change is ledgered | `InventoryItem` | D: `InventoryItemTests` · A: `InventoryCommandsTests` · I: `InventoryAndOrderTests` | — |
| Checkout reserves, payment commits, cancellation releases | `InventoryReservations`, `OrderPaymentConfirmation` | A: `InventoryReservationsTests`, `ConfirmOrderPaymentHandlerTests` · I: `InventoryAndOrderTests` | — |
| Abandoned checkouts are settled | `ExpireStaleCheckoutsCommand` + the hosted sweep | A: `ExpireStaleCheckoutsHandlerTests` · I: `InventoryAndOrderTests` | The sweep skips stores that are not Active (R-24) |
| Low stock raises an event | `InventoryItem`, `StockBecameLowHandler` | D: `DomainEventTests` · I: `NotificationTests` | No low-stock email exists (the ADR expected one) |

## 4. Shopping, orders and money

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| A guest basket survives and merges at sign-in | `BasketResolver` | A: `BasketHandlersTests` · I: `BasketTests` | — |
| One pricing pipeline serves the basket and checkout | `PricingService` | A: `PricingServiceTests`, `CreateOrderHandlerTests` · I: `BasketTests` | Tax is a fixed zero (P-06) |
| Coupon limits hold under concurrency | `Coupon`, `CouponRedemptions` | D: `CouponTests`, `CouponRuleMatrixTests` · A: `CouponRedemptionsTests` · I: `CouponRedemptionTests` | A second, weaker coupon path exists at `/api/coupons/apply` (TD-06) |
| Shipping is chosen, priced and snapshotted | `StoreShippingRates`, `Order.ApplyShipping` | D: `ShippingMethodTests`, `OrderShippingTests` · A: `StoreShippingRatesTests` · I: `ShippingTests` | — |
| Orders are numbered per store and immutable after placement | `Order`, `OrderNumbers` | D: `OrderTests`, `OrderLifecycleTests` · I: `OrderLifecycleTests` | — |
| Only allowed status transitions happen, with the actor recorded | `OrderTransitions` | D: `OrderLifecycleTests` · A: `UpdateOrderStatusHandlerTests` · I: `OrderLifecycleTests` | Order-status changes are not audited |
| Payment confirmation is idempotent from both doors, and reads the intent's state rather than a boolean | `OrderPaymentConfirmation`, `PaymentIntentState` | A: `ConfirmOrderPaymentHandlerTests`, `ProcessPaymentWebhookHandlerTests` · I: `PaymentsAndRefundsTests` | The mapping of real Stripe statuses onto the four states is unverified against a live account ([ADR-0036](../11-ADR/0036-payment-intent-state-machine.md)) |
| Refunds cannot exceed the capture and are retry-safe | `Payment`, `OrderPayments` | D: `PaymentTests` · A: `OrderPaymentsTests` · I: `PaymentsAndRefundsTests` | The Stripe adapter itself has no test (TD-33) |
| Money keeps its currency and minor units | `Money`, `CurrencyInfo`, `StripeAmountConverter` | D: `MoneyTests` · I: `StripeAmountConverterTests`, `InventoryAndOrderTests` | **JOD minor units at Stripe are unverified against a real account (P-05, R-01)** |

## 5. Customers, reviews, notifications

| Capability | Implementation | Tests | Gaps |
|---|---|---|---|
| A customer profile is separate from the login account | `Customer`, `RegisterHandler` | D: `CustomerProfileTests` · I: `CustomerAccountTests` | The two modules form a cycle (TD-03) |
| Address book with defaults | `Customer`, the account use cases | D: `CustomerProfileTests` · I: `CustomerAccountTests` | `Customer` has no concurrency token |
| Blocked customers cannot buy | `Customer.IsBlocked` checks | A: `CreateOrderHandlerTests`, `CustomerHandlersTests` · I: `CustomerAccountTests` | A blocked customer can still fill a basket and a wishlist |
| Export and erasure keep orders and anonymize the person | `CustomerErasure` | A: `CustomerHandlersTests` · I: `CustomerAccountTests` | In-app notifications survive erasure |
| Reviews require a verified purchase, one per product | `Review`, `CreateReviewHandler` | D: `ReviewTests` · A: `CreateReviewHandlerTests` · I: `ReviewModerationTests` | — |
| Moderation follows the store's policy and is audited | `ReviewModeration`, `Tenant.ReviewsAutoApprove` | A: `ReviewModerationHandlersTests` · I: `ReviewModerationTests` | No moderation notifications (deferred) |
| Nothing is lost or sent twice after a commit | the outbox | A: `OutboxPolicyTests` · I: `NotificationTests` | Purge, lease expiry and concurrent dispatchers are untested (TD-34) |
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

## 7. Where traceability stops

Be aware of these when you plan work:

1. **CI runs but does not yet block merges.** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs the .NET suites and the frontend lint, type-check, tests and build on every push; until branch protection is switched on, a red run can still be merged (TD-31). The Playwright journeys are not in CI at all.
2. **Frontend component coverage is partial**: the guards, the error boundary, the account shell, the order screens and checkout's money barriers are tested; form-level interaction and most admin screens are verified only by the Playwright journeys or by hand (TD-32).
3. **External adapters are untested**: Stripe and the three email providers are exercised only through fakes (TD-33).
4. **Some rules are untested**, listed in the gaps section of [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) — notably the sweeps for non-active stores, the password policy itself, and several length limits.
5. **Performance is unmeasured**: no load test, no query budget beyond the N+1 checks.
6. **Two capabilities the documentation once promised do not exist**: low-stock emails and moderation notifications. They are marked DEFERRED in their module documents rather than quietly dropped.
