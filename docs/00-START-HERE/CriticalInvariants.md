# Do not break these rules

> **What this page is:** the invariants Souq depends on, each with its reason and the place that enforces it. Keep it open while you change code or review a change.
> **How it relates to the other rule pages:**
> - `AGENTS.md` is the working contract: §0 are the overriding rules, §3 the structural rules and their tests.
> - [SecurityControls.md](../07-SECURITY/SecurityControls.md) is the full control catalogue with its gaps.
> - This page is the cross-cutting summary: what must stay true, and where to look when you are about to touch it.
>
> **Level:** L2. **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`.

**How to read the "Enforced by" column:** a test name means `dotnet test` or `npx vitest run` fails when you break the rule; the test file sits under `tests/` or `frontend/src/`. "Review" means nothing mechanical catches it, so the reviewer is the last line of defence. If an enforcing test fails after your change, **the control is working and your change is wrong**. Never weaken the test (`AGENTS.md` §0.2).

## 1. Tenant isolation

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| The store is resolved from the `Host` header on the server; no request type carries a `TenantId` outside the platform area | A client that can name a store can name someone else's | `TenantResolutionMiddleware`; `ModuleAndContractRuleTests`; `TenantResolutionTests` | [ADR-0006](../11-ADR/0006-tenant-resolution.md) |
| Every tenant-owned entity has the tenant query filter and a foreign key to `Tenants` | A forgotten `Where` must not leak rows | `AppDbContext`; `TenancyRuleTests` | [ADR-0022](../11-ADR/0022-tenancy-enforcement.md) |
| No write lands in another store | A bug that loaded the wrong row must not save it | `TenantWriteGuardInterceptor`; `TenantIsolationTests` | [ADR-0022](../11-ADR/0022-tenancy-enforcement.md) |
| `IgnoreQueryFilters` only in `PlatformQueries`; no raw SQL outside migrations; bulk `ExecuteUpdate`/`ExecuteDelete` only in reviewed places | They bypass the filter or the write guard | `TenancyRuleTests` (ADO.NET commands are outside its reach: review) | [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) |
| Another store's or another customer's id answers `404`, never `403` | A `403` confirms the id exists | Use cases and filtered repositories; `TenantIsolationTests` (every id-bearing endpoint must be in its table) | [ADR-0022](../11-ADR/0022-tenancy-enforcement.md) |
| An access token only works on the host it was issued for | A store admin's token must not work on another store or the platform | `AccessTokenValidation`; `AuthSessionTests`, `ProvisioningBoundaryTests` | [ADR-0006](../11-ADR/0006-tenant-resolution.md) |
| Background work runs inside an explicit store scope | A job with no scope has no filter | `StoreSweepService`, `ITenantScopeRunner`; review | [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) |

## 2. Authorization: the backend decides

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| Every endpoint declares its access (`[HasPermission]`, `[Authorize]` or a deliberate `[AllowAnonymous]`) | Access by omission is how endpoints leak | `EndpointRuleTests` | [ADR-0019](../11-ADR/0019-authorization-foundation.md) |
| Roles grant permissions in one place, `RolePermissions` in `src/Souq.Application/Common/Security/Permissions.cs` | Two tables drift | `AuthorizationMatrixTests` (role × endpoint over HTTP) | [ADR-0010](../11-ADR/0010-authentication-authorization.md) |
| Ownership (a customer's own orders, addresses, reviews) is checked inside the use case | An attribute cannot know who owns row 42 | Handlers; `TenantIsolationTests`, `CustomerAccountTests` | [AuthenticationAndAuthorization.md](../07-SECURITY/AuthenticationAndAuthorization.md) |
| The frontend never authorizes: guards, hidden menus and `can()` are user experience | A user can call the API directly | Review; every server test above | [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md) |

## 3. Platform and store boundaries

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| Platform endpoints (`[PlatformEndpoint]`) answer only on the platform host; store endpoints only on store hosts | The platform area must not exist on a customer-facing domain | `TenantAvailabilityMiddleware`; `AuthorizationBoundaryTests` | [ADR-0024](../11-ADR/0024-platform-administration.md) |
| Platform endpoints require a `platform.*` permission, and every platform request is audited | The platform is the only area that names a store in the route | `EndpointRuleTests`, `ModuleAndContractRuleTests` | [ADR-0024](../11-ADR/0024-platform-administration.md) |
| The platform provisions a store and invites its administrator, but cannot act as a store user | A platform account inside a store's admin is an unaudited back door | Host-bound tokens; `ProvisioningBoundaryTests`, `frontend/e2e/platform-provisioning.spec.js` | [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) (support mode is FUTURE) |
| A closed store (`Provisioning`, `Suspended`, `Archived`) serves only what is marked `[AvailableWhenStoreClosed]`: its configuration and sign-in | A suspended store must not sell; its branded closed page must still render | `TenantAvailabilityMiddleware`; `PlatformAdministrationTests` | [ADR-0022](../11-ADR/0022-tenancy-enforcement.md) |
| Nothing lets a request past the store-status gate without an owner decision (storefront preview) | A preview credential opens a closed store | Not built: [StorefrontPreview.md](../04-MODULES/Platform/StorefrontPreview.md), D-22 | [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) |

## 4. Authentication and sessions

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| The access token lives in memory; the refresh token in an `HttpOnly`, `SameSite=Strict` cookie scoped to `/api/auth`; nothing in browser storage | A script must not be able to read a credential | `AuthController`, `frontend/src/api/client.js`; `CookieSecurityTests` | [ADR-0023](../11-ADR/0023-sessions-and-credentials.md) |
| Refresh tokens rotate; reusing an old one revokes the family | A stolen token must stop working | `RefreshSession.cs`; `AuthSessionTests` | [ADR-0023](../11-ADR/0023-sessions-and-credentials.md) |
| A password change or a disabled account invalidates every access token at once (security stamp) | Waiting for expiry leaves a window | `AccessTokenValidation`; `AuthSessionTests` | [ADR-0010](../11-ADR/0010-authentication-authorization.md) |
| Unknown email and wrong password look identical, in message and timing; lockout after repeated failures; sign-in rate-limited | No account enumeration, no brute force | `LoginHandler`, `User.RecordFailedLogin`, `RateLimiting.cs`; `UserTests`, `AuthHandlersTests` | [AuthenticationAndAuthorization.md](../07-SECURITY/AuthenticationAndAuthorization.md) |
| The last active administrator of a store, or the last platform owner, cannot be disabled, even by two concurrent requests | A scope with nobody to run it | `AccountStatusChanger`; `LastAdministratorConcurrencyTests` | [Identity module](../04-MODULES/Identity/README.md) |

## 5. Secrets and configuration

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| No secret in any committed `appsettings*.json`; `.env.example` holds no usable value | Repositories leak | `ConfigurationSourceTests`; the CI secret scan in `.github/workflows/ci.yml` | [ADR-0020](../11-ADR/0020-configuration-and-secrets.md) |
| Never read, print or commit `.env` | It holds the owner's real secrets | Review (`AGENTS.md` §0.8) | [ADR-0020](../11-ADR/0020-configuration-and-secrets.md) |
| The API refuses to start with a weak signing key, no email provider, no payment provider (unless the fake one is explicitly chosen) or an invalid secrets key | A half-configured production silently loses email or takes no payments | Startup validation in `Program.cs`; `StartupAndSecurityTests`, `ConfigurationTests` | [Configuration.md](../09-OPERATIONS/Configuration.md) |
| Stores' payment keys are stored encrypted, bound to their store, and never returned | A database leak must not leak Stripe keys | `AesGcmSecretProtector`; `PaymentAdapterTests`, `PaymentsAndRefundsTests` | [ADR-0031](../11-ADR/0031-payments-and-refunds.md) |
| No token, password, card data or personal data in logs | Logs are copied everywhere | Request logging rules; `ObservabilityTests`; review | [ADR-0018](../11-ADR/0018-observability.md) |

## 6. Payments and money

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| Totals, discounts, shipping and stock are computed on the server by one pricing pipeline; the client only displays them | Two calculators disagree, and a browser can be edited | `PricingService`; `BasketTests`, `PricingServiceTests` | [ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md) |
| An order's totals are frozen when it is placed | A later price change must not rewrite what the customer agreed to pay | `Order`; `OrderLifecycleTests` | [ADR-0029](../11-ADR/0029-orders-lifecycle.md) |
| An order line is one variant: it references the variant bought and freezes its SKU and label; two variants are never merged; a client-named variant is accepted only from the product it names, active; variants are deactivated, never deleted | An invoice must say exactly what was sold, and one product must never be priced with another's variant | `Order.AddItem`, `Product.CanSell`, unique `(OrderId, VariantId)`; `OrderTests`, `ProductVariantTests`, `TenantIsolationTests` | [ADR-0039](../11-ADR/0039-product-variants-order-identity.md) |
| Money is a `Money` value (amount + currency), stored at fixed precision and rounded to the currency's minor units; currencies never mix | Floating point and mixed currencies lose money silently | `Money`, `CurrencyInfo`; `MoneyTests` | [ADR-0014](../11-ADR/0014-money-precision.md) |
| No card data is ever stored; payment tables only hold reviewed columns | PCI scope | `PaymentDataRulesTests` | [ADR-0031](../11-ADR/0031-payments-and-refunds.md) |
| Webhooks are signature-verified and routed to the store that created the payment | A forged webhook must not mark an order paid | Payment gateway adapter; `PaymentsAndRefundsTests`, `PaymentAdapterTests` | [ADR-0031](../11-ADR/0031-payments-and-refunds.md) |
| The payment intent follows its state machine; a declined card never captures against a cancelled order | The R-02 defect | [ADR-0036](../11-ADR/0036-payment-intent-state-machine.md); `PaymentsAndRefundsTests` | [ADR-0036](../11-ADR/0036-payment-intent-state-machine.md) |
| Payment behaviour (amounts, capture, cancellation, refunds, retries) never changes silently: only deliberately, with an ADR and tests | It is commercial behaviour | Review (`AGENTS.md` §0.3) | — |
| JOD and other three-decimal currencies are sent to Stripe as ×100 today; whether that is right is **unverified** | Wrong by a factor of ten, silently | `StripeAmountConverter`; `StripeAmountConverterTests` encodes ×100 only | Owner decision P-05 |

## 7. Stock, concurrency and idempotency

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| Stock is never decremented directly: checkout reserves, payment commits, cancellation or expiry releases, every change writes a ledger line; available never goes negative | The last item sells once | `InventoryItem`; `InventoryItemTests`, `InventoryAndOrderTests` | [ADR-0026](../11-ADR/0026-inventory-reservations.md) |
| Contended rows carry a `rowversion`; a lost race is a `409 ConcurrencyConflict` or a retry, never a silent overwrite | Two editors, one row | EF configurations (`HasRowVersion`); `GlobalExceptionHandler` | [ADR-0013](../11-ADR/0013-optimistic-concurrency.md) |
| The last coupon use is taken once under concurrency | A limited coupon is a promise | `ICouponRedemptions`; `CouponRedemptionTests` | [ADR-0030](../11-ADR/0030-coupon-redemptions.md) |
| Refunds are idempotent and cannot exceed the payment, even when requests race | Money leaves once | Idempotency keys; `PaymentsAndRefundsTests` | [ADR-0031](../11-ADR/0031-payments-and-refunds.md) |
| Payment confirmation arrives twice (browser and webhook) and both paths are idempotent | Either may arrive first, or alone | `OrderPaymentConfirmation`; `PaymentsAndRefundsTests` | [FeatureMaps.md](../04-MODULES/FeatureMaps.md) |
| A duplicate checkout submission is **not** idempotent today; what it should return is undecided | Measured, not assumed | `CheckoutIdempotencyTests` documents today's behaviour | Owner decision F-8 |

## 8. Transactions and side effects

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| No network call (Stripe, email) while a database transaction is open | A slow provider holds locks; a rollback can't undo an email | Review; the refund-after-commit pattern in `UpdateOrderStatusHandler` | [ADR-0021](../11-ADR/0021-transaction-boundaries.md) |
| Anything that leaves the system after a commit goes through the outbox, written in the same transaction | Nothing sent for a rolled-back change, nothing lost on a crash | `AppDbContext` (events → `OutboxMessage`); only notification handlers use `IEmailSender` (`ModuleAndContractRuleTests`); `NotificationTests` | [ADR-0034](../11-ADR/0034-notifications-outbox.md) |
| No code reads the clock directly; time is injected (`TimeProvider`) | Expiry, lockout and validity windows must be testable | `ClockRuleTests`; `AuditTimestampsTests` | [ADR index §4](../11-ADR/README.md) (the clock port became .NET's `TimeProvider`) |

## 9. Time: UTC everywhere

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| Instants are stored in UTC (`datetime2`, stamped by the context) | One store's "now" is another's yesterday | `TimeProvider.GetUtcNow`; `ClockRuleTests`, `AuditTimestampsTests` | [ADR-0007](../11-ADR/0007-database-strategy.md) |
| Every instant in an API response ends in `Z` | Without it a browser reads the time in its own zone, and every time shows shifted by the reader's offset | `UtcDateTimeJsonConverter`; `PlatformAuditViewerTests` | [ApiDocumentation.md](../05-API/ApiDocumentation.md) |
| Dates are displayed in the store's region and time zone, in the reader's language, with Latin digits | The merchant and the customer must see the same order time | `frontend/src/app/dateLocale.js`; `dateLocale.test.js` | [WhiteLabel.md](../08-FRONTEND/WhiteLabel.md) |

## 10. Audit logging

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| The audit log is append-only: no update, no delete | An audit log that can be edited proves nothing | `TenantWriteGuardInterceptor`; `PlatformAdministrationTests` | [ADR-0024](../11-ADR/0024-platform-administration.md) |
| The audit line is written in the same unit of work as the change, and dropped when the request fails | No change without its line, no line without its change | `AuditBehavior` | [ADR-0024](../11-ADR/0024-platform-administration.md) |
| Audit metadata is chosen field by field; request bodies are never copied | Bodies carry passwords and tokens | `IAuditable.ToAuditRecord` per request; review | [Platform module](../04-MODULES/Platform/README.md) |
| The activity log shows what a line records and no more: ids and roles rather than fetched names, and on-screen notice that refused or failed requests, sign-ins and shoppers' actions aren't recorded | An absence must not read as evidence | `frontend/src/pages/platform/Audit.jsx`; `Audit.test.jsx` | [Platform module](../04-MODULES/Platform/README.md) |
| Store-side order, coupon and shipping changes are **not** audited today | Recorded debt, not an oversight | TD-12 in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) | — |

## 11. Destructive and irreversible actions

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| Every destructive action in the UI confirms in `ConfirmDialog`; archiving a store requires typing its slug; the server's refusal is read inside the dialog | A misclick must not delete or disable | `useConfirmAction`; `useConfirmAction.test.jsx`, `frontend/e2e/back-office.spec.js` | [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md) |
| A migration that drops, renames or narrows data needs the owner's explicit approval: write it, don't run it | Migrations run at startup, so a destructive one destroys data at deploy time | `MigrationSafetyTests` (a new destructive migration fails until registered); `AGENTS.md` §0.6 | [Migrations.md](../06-DATABASE/Migrations.md) |
| Nothing deletes a store (`TenantRepository.Remove` throws); archiving is terminal | Orders and invoices must survive | `Tenant`; `TenantTests` | [Platform module](../04-MODULES/Platform/README.md) |

## 12. White-label, languages and accessibility

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| No brand name, currency or store contact literal in product code or committed configuration; everything comes from the store's configuration at runtime | One build serves every store | `WhiteLabelSourceTests`, `whiteLabel.test.js` | [ADR-0035](../11-ADR/0035-white-label-runtime.md) |
| Every user-visible string is translated in both `en.json` and `ar.json`, with the same keys and Arabic plural forms | A missing key shows its name to a customer | `translationKeys.test.js`, `locales.test.js` | [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md) |
| Right-to-left works by construction: logical CSS properties, directional icons, names isolated inside sentences with the `bidi` formatter | An Arabic name must not scramble English punctuation, and vice versa | `rtl.test.js`, `bidi.test.js` | [DesignSystem.md](../08-FRONTEND/DesignSystem.md) |
| Text keeps WCAG AA contrast in light and dark mode for every store's colours; controls have accessible names; dialogs trap Escape and return focus | Accessibility is a product requirement, and store palettes vary | `BrandColors` contrast rules; `tenantModel.test.js`, `a11y.test.jsx`; axe in `frontend/e2e` | [DesignSystem.md](../08-FRONTEND/DesignSystem.md) |
| A module a store turned off is enforced on the server (`[RequiresModule]` and in the use case), not only hidden | Hidden isn't disabled | `TenantAvailabilityMiddleware`; `PlatformAdministrationTests` | [ADR-0024](../11-ADR/0024-platform-administration.md) |

## 13. Honest data

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| No fake business data: demo catalogues are seeded only in Development/Testing or on explicit request, never into a production database | A production store showing invented products or orders is a lie to its customers | `SeedSafetyTests` | [SeedAndBootstrap.md](../09-OPERATIONS/SeedAndBootstrap.md) |
| Dashboards show only what the data can support; profit, margin, conversion and forecasts are declared absent, not estimated | A plausible invented number is worse than a gap | Reporting queries; `StoreDashboardTests` | [Dashboards.md](../04-MODULES/Reporting/Dashboards.md) |
| Don't invent business rules | A guessed rule looks deliberate to everyone after you | Review (`AGENTS.md` §0.4) | [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) |

## 14. Architecture and dependencies

| Rule | Why | Enforced by | Decision |
|---|---|---|---|
| Dependencies point inwards: API → Infrastructure → Application → Domain; the Domain references nothing | Business rules outlive technologies | `DependencyRuleTests` | [ADR-0003](../11-ADR/0003-clean-hexagonal-boundaries.md) |
| A module reaches another only through its `Contracts`; no cycles; no new cross-module domain crossing | Modules stay separable | `ModuleAndContractRuleTests`; the generated ratchet [ModuleDomainDependencies.md](../02-ARCHITECTURE/ModuleDomainDependencies.md) | [ADR-0004](../11-ADR/0004-module-boundaries.md) |
| No entity in a request or response; no `IQueryable` outside Infrastructure | Leaks internals and tenant-unfiltered composition | `ModuleAndContractRuleTests` | [CQRS.md](../02-ARCHITECTURE/CQRS.md) |
| No microservices, message broker, event sourcing, database per store or tenant, second ORM, Kubernetes, or replacement of SQL Server or React without an ADR superseding the recorded reason | These were evaluated and rejected with evidence | Review (`AGENTS.md` §0.5) | [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) |
| A new dependency is a proposal, not a quiet addition; MediatR stays on 12.x | Licences and supply chain (MediatR 13+ is commercial) | Review; CI dependency audit | [ADR-0015](../11-ADR/0015-testing-strategy.md) (licensing precedent), `AGENTS.md` §5 |

## 15. Testing and documentation

| Rule | Why | Enforced by |
|---|---|---|
| Never weaken a test or the control it protects to make a suite pass | The test is the memory of a decision | Review (`AGENTS.md` §0.2) |
| A feature touching money, stock or personal data has an integration test; an id-bearing endpoint has an isolation row | Those are the expensive failures | `TenantIsolationTests` (coverage check); review |
| Backticked names and paths in current documentation exist; links and anchors resolve; ADRs are complete and indexed; generated inventories match the code | Documentation that lies is worse than none | `DocumentationTests`, `GeneratedDocsTests` |
| The gate in [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md) passes before a change is called done | CI mirrors it, but doesn't yet block merges | `.github/workflows/ci.yml` |
