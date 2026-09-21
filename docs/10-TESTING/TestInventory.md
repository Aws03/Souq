# Test inventory

> **Generated from the test sources** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). Do not edit by hand; regenerate with:
> `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
>
> Counts are test *methods* (`[Fact]`, `[Theory]`, Vitest `it`/`test`), not executed cases: a theory runs once per data row, so `dotnet test` reports more. What each suite is for: [TestingStrategy.md](TestingStrategy.md).

| Suite | Files | Facts | Theories |
|---|---|---|---|
| [`Souq.Domain.Tests`](#souqdomaintests) | 30 | 254 | 50 |
| [`Souq.Application.Tests`](#souqapplicationtests) | 52 | 387 | 19 |
| [`Souq.ArchitectureTests`](#souqarchitecturetests) | 17 | 76 | 8 |
| [`Souq.IntegrationTests`](#souqintegrationtests) | 59 | 370 | 27 |
| [frontend (Vitest)](#frontend-vitest) | 95 | 724 | — |
| [frontend (Playwright)](#frontend-playwright) | 19 | 126 | — |

## Souq.Domain.Tests

| File | Classes | Facts | Theories |
|---|---|---|---|
| `tests/Souq.Domain.Tests/BasketTests.cs` | `BasketTests` | 9 | 1 |
| `tests/Souq.Domain.Tests/CatalogSearchProjectionTests.cs` | `CatalogSearchProjectionTests` | 8 | 0 |
| `tests/Souq.Domain.Tests/CouponRuleMatrixTests.cs` | `CouponRuleMatrixTests` | 2 | 2 |
| `tests/Souq.Domain.Tests/CouponTests.cs` | `CouponTests` | 12 | 1 |
| `tests/Souq.Domain.Tests/CustomerProfileTests.cs` | `CustomerProfileTests` | 9 | 1 |
| `tests/Souq.Domain.Tests/DomainEventTests.cs` | `DomainEventTests` | 5 | 0 |
| `tests/Souq.Domain.Tests/DomainExceptionCodeTests.cs` | `DomainExceptionCodeTests` | 1 | 1 |
| `tests/Souq.Domain.Tests/InventoryItemTests.cs` | `InventoryItemTests` | 11 | 2 |
| `tests/Souq.Domain.Tests/InvitationAndAuditTests.cs` | `InvitationAndAuditTests` | 5 | 1 |
| `tests/Souq.Domain.Tests/MoneyTests.cs` | `MoneyTests` | 13 | 3 |
| `tests/Souq.Domain.Tests/OrderLifecycleTests.cs` | `OrderLifecycleTests` | 6 | 1 |
| `tests/Souq.Domain.Tests/OrderShippingTests.cs` | `OrderShippingTests` | 5 | 0 |
| `tests/Souq.Domain.Tests/OrderTests.cs` | `OrderTests` | 28 | 2 |
| `tests/Souq.Domain.Tests/PaymentTests.cs` | `PaymentTests` | 5 | 3 |
| `tests/Souq.Domain.Tests/PlanAndEntitlementTests.cs` | `PlanAndEntitlementTests` | 20 | 3 |
| `tests/Souq.Domain.Tests/ProductOptionTests.cs` | `ProductOptionTests` | 19 | 0 |
| `tests/Souq.Domain.Tests/ProductTests.cs` | `ProductTests`, `CategoryTests` | 11 | 1 |
| `tests/Souq.Domain.Tests/ProductVariantTests.cs` | `ProductVariantTests`, `ProductCostTests` | 17 | 0 |
| `tests/Souq.Domain.Tests/RefreshTokenTests.cs` | `RefreshTokenTests` | 3 | 0 |
| `tests/Souq.Domain.Tests/ReviewTests.cs` | `ReviewTests` | 8 | 1 |
| `tests/Souq.Domain.Tests/SearchDistanceTests.cs` | `SearchDistanceTests` | 7 | 3 |
| `tests/Souq.Domain.Tests/SearchSynonymTests.cs` | `SearchSynonymTests` | 7 | 3 |
| `tests/Souq.Domain.Tests/SearchTextTests.cs` | `SearchTextTests` | 5 | 6 |
| `tests/Souq.Domain.Tests/ShippingMethodTests.cs` | `ShippingMethodTests` | 4 | 1 |
| `tests/Souq.Domain.Tests/StorePaymentAccountTests.cs` | `StorePaymentAccountTests` | 3 | 2 |
| `tests/Souq.Domain.Tests/StoreSettingsTests.cs` | `StoreSettingsTests` | 5 | 3 |
| `tests/Souq.Domain.Tests/TenantSettingsTests.cs` | `TenantSettingsTests` | 7 | 1 |
| `tests/Souq.Domain.Tests/TenantTests.cs` | `TenantTests` | 7 | 4 |
| `tests/Souq.Domain.Tests/UserTests.cs` | `UserTests`, `CustomerTests` | 11 | 3 |
| `tests/Souq.Domain.Tests/WishlistItemTests.cs` | `WishlistItemTests` | 1 | 1 |

## Souq.Application.Tests

| File | Classes | Facts | Theories |
|---|---|---|---|
| `tests/Souq.Application.Tests/Auth/AccountLifecycleTests.cs` | `AccountLifecycleTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Auth/AccountWriterTests.cs` | `AccountWriterTests` | 6 | 0 |
| `tests/Souq.Application.Tests/Auth/AuthHandlersTests.cs` | `AuthRig`, `RegisterHandlerTests`, `LoginHandlerTests`, `RefreshSessionHandlerTests`, `LogoutHandlerTests`, `ChangePasswordHandlerTests`, `ForgotPasswordHandlerTests`, `ResetPasswordHandlerTests`, `VerifyEmailHandlerTests` | 27 | 0 |
| `tests/Souq.Application.Tests/Auth/PasswordCheckTests.cs` | `PasswordCheckTests` | 3 | 0 |
| `tests/Souq.Application.Tests/Auth/PasswordRulesTests.cs` | `PasswordRulesTests` | 4 | 2 |
| `tests/Souq.Application.Tests/Baskets/BasketCheckoutTests.cs` | `BasketCheckoutTests` | 3 | 0 |
| `tests/Souq.Application.Tests/Baskets/BasketHandlersTests.cs` | `BasketHandlersTests` | 13 | 0 |
| `tests/Souq.Application.Tests/Baskets/BasketWriterTests.cs` | `BasketWriterTests` | 6 | 0 |
| `tests/Souq.Application.Tests/Baskets/PricingServiceTests.cs` | `PricingServiceTests` | 11 | 0 |
| `tests/Souq.Application.Tests/Billing/BillingHandlerTests.cs` | `BillingHandlerTests`, `FixedClock` | 9 | 0 |
| `tests/Souq.Application.Tests/Categories/CategoryHandlersTests.cs` | `CreateCategoryHandlerTests`, `UpdateCategoryHandlerTests`, `DeleteCategoryHandlerTests` | 13 | 0 |
| `tests/Souq.Application.Tests/Common/AccountsTests.cs` | `AccountsTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Common/AuditBehaviorTests.cs` | `AuditBehaviorTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Common/MediaFileInspectorTests.cs` | `MediaFileInspectorTests`, `NonSeekableStream` | 3 | 2 |
| `tests/Souq.Application.Tests/Common/PagingValidatorTests.cs` | `PagingValidatorTests` | 6 | 1 |
| `tests/Souq.Application.Tests/Common/TenantContextTests.cs` | `TenantContextTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Common/UseCaseLoggingBehaviorTests.cs` | `UseCaseLoggingBehaviorTests`, `SteppingClock`, `ListLoggerFactory`, `ListLogger` | 3 | 0 |
| `tests/Souq.Application.Tests/Coupons/CouponHandlersTests.cs` | `CouponHandlersTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Coupons/CouponRedemptionsTests.cs` | `CouponRedemptionsTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Customers/CustomerAccountProfilesTests.cs` | `CustomerAccountProfilesTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Customers/CustomerHandlersTests.cs` | `CustomerAccountHandlersTests`, `CreateOrderCustomerRulesTests`, `AddressInputTestExtensions` | 9 | 0 |
| `tests/Souq.Application.Tests/Inventory/InventoryCommandsTests.cs` | `InventoryCommandsTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Inventory/InventoryReservationsTests.cs` | `InventoryReservationsTests` | 9 | 0 |
| `tests/Souq.Application.Tests/Notifications/NotificationHandlersTests.cs` | `OutboxPolicyTests`, `IdentityEmailHandlersTests`, `OrderNotificationHandlersTests`, `NotificationUseCasesTests` | 16 | 1 |
| `tests/Souq.Application.Tests/Observability/SouqMetricsTests.cs` | `SouqMetricsTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Orders/CancelMyOrderHandlerTests.cs` | `CancelMyOrderHandlerTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Orders/ConfirmOrderPaymentHandlerTests.cs` | `ConfirmOrderPaymentHandlerTests` | 12 | 0 |
| `tests/Souq.Application.Tests/Orders/CreateOrderHandlerTests.cs` | `CreateOrderHandlerTests` | 17 | 1 |
| `tests/Souq.Application.Tests/Orders/ExpireStaleCheckoutsHandlerTests.cs` | `ExpireStaleCheckoutsHandlerTests` | 6 | 0 |
| `tests/Souq.Application.Tests/Orders/GetOrderByIdHandlerTests.cs` | `GetOrderByIdHandlerTests` | 7 | 1 |
| `tests/Souq.Application.Tests/Orders/ProcessPaymentWebhookHandlerTests.cs` | `ProcessPaymentWebhookHandlerTests`, `ApplyPaymentEventHandlerTests` | 6 | 2 |
| `tests/Souq.Application.Tests/Orders/UpdateOrderStatusHandlerTests.cs` | `UpdateOrderStatusHandlerTests` | 9 | 2 |
| `tests/Souq.Application.Tests/Payments/OrderPaymentsTests.cs` | `OrderPaymentsTests` | 10 | 0 |
| `tests/Souq.Application.Tests/Payments/StorePaymentAccountEditorTests.cs` | `StorePaymentAccountEditorTests` | 5 | 1 |
| `tests/Souq.Application.Tests/Platform/TenantAdministrationTests.cs` | `TenantAdministrationTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Products/GetProductsHandlerTests.cs` | `GetProductsHandlerTests` | 6 | 0 |
| `tests/Souq.Application.Tests/Products/GetRelatedProductsHandlerTests.cs` | `GetRelatedProductsHandlerTests` | 3 | 0 |
| `tests/Souq.Application.Tests/Products/ProductHandlersTests.cs` | `CreateProductHandlerTests`, `UpdateProductHandlerTests`, `ProductLifecycleHandlerTests`, `GetProductByIdHandlerTests` | 17 | 0 |
| `tests/Souq.Application.Tests/Products/ProductVariantHandlersTests.cs` | `ProductVariantHandlersTests` | 14 | 0 |
| `tests/Souq.Application.Tests/Products/PurgeSearchLogHandlerTests.cs` | `PurgeSearchLogHandlerTests` | 4 | 0 |
| `tests/Souq.Application.Tests/Products/SearchSynonymHandlersTests.cs` | `CreateSearchSynonymHandlerTests`, `UpdateSearchSynonymHandlerTests`, `DeleteSearchSynonymHandlerTests` | 9 | 0 |
| `tests/Souq.Application.Tests/Products/UploadProductMediaHandlerTests.cs` | `UploadProductMediaHandlerTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Reporting/StoreDashboardTests.cs` | `StoreDashboardWindowTests`, `StoreDashboardLocalWindowTests`, `LetExtensions` | 13 | 3 |
| `tests/Souq.Application.Tests/Reviews/CreateReviewHandlerTests.cs` | `CreateReviewHandlerTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Reviews/ReviewModerationHandlersTests.cs` | `ReviewModerationHandlersTests` | 6 | 0 |
| `tests/Souq.Application.Tests/Security/RolePermissionsTests.cs` | `RolePermissionsTests` | 8 | 0 |
| `tests/Souq.Application.Tests/Shipping/ShippingMethodHandlersTests.cs` | `ShippingMethodHandlersTests` | 3 | 0 |
| `tests/Souq.Application.Tests/Shipping/StoreShippingRatesTests.cs` | `StoreShippingRatesTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Stores/ReviewSettingsHandlersTests.cs` | `ReviewSettingsHandlersTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Stores/StoreBrandingSettingsTests.cs` | `StoreBrandingSettingsTests` | 6 | 1 |
| `tests/Souq.Application.Tests/Stores/StoreSettingsOptionsTests.cs` | `StoreSettingsOptionsTests` | 4 | 1 |
| `tests/Souq.Application.Tests/Wishlist/WishlistHandlersTests.cs` | `WishlistHandlersTests` | 9 | 1 |

## Souq.ArchitectureTests

| File | Classes | Facts | Theories |
|---|---|---|---|
| `tests/Souq.ArchitectureTests/BackupVerificationScriptTests.cs` | `BackupVerificationScriptTests`, `TempDirectory` | 10 | 1 |
| `tests/Souq.ArchitectureTests/ClockRuleTests.cs` | `ClockRuleTests` | 0 | 1 |
| `tests/Souq.ArchitectureTests/ConfigurationSourceTests.cs` | `ConfigurationSourceTests` | 2 | 1 |
| `tests/Souq.ArchitectureTests/ContentSecurityPolicyTests.cs` | `ContentSecurityPolicyTests` | 4 | 0 |
| `tests/Souq.ArchitectureTests/DependencyRuleTests.cs` | `DependencyRuleTests` | 8 | 0 |
| `tests/Souq.ArchitectureTests/DocumentationTests.cs` | `DocumentationTests` | 7 | 0 |
| `tests/Souq.ArchitectureTests/EndpointRuleTests.cs` | `EndpointRuleTests` | 3 | 0 |
| `tests/Souq.ArchitectureTests/GeneratedDocsTests.cs` | `GeneratedDocsTests` | 5 | 1 |
| `tests/Souq.ArchitectureTests/MigrationSafetyTests.cs` | `MigrationSafetyTests` | 3 | 0 |
| `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs` | `ModuleAndContractRuleTests` | 11 | 1 |
| `tests/Souq.ArchitectureTests/NotificationDocumentationTests.cs` | `NotificationDocumentationTests` | 2 | 0 |
| `tests/Souq.ArchitectureTests/OperationalScriptTests.cs` | `OperationalScriptTests`, `TempEnv` | 3 | 3 |
| `tests/Souq.ArchitectureTests/QuotaRuleTests.cs` | `QuotaRuleTests`, `MerchantCostSecrecyTests` | 5 | 0 |
| `tests/Souq.ArchitectureTests/SearchRuntimeRuleTests.cs` | `SearchRuntimeRuleTests` | 1 | 0 |
| `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` | `TenancyRuleTests` | 8 | 0 |
| `tests/Souq.ArchitectureTests/ValidationRuleTests.cs` | `ValidationRuleTests` | 2 | 0 |
| `tests/Souq.ArchitectureTests/WhiteLabelSourceTests.cs` | `WhiteLabelSourceTests` | 2 | 0 |

## Souq.IntegrationTests

| File | Classes | Facts | Theories |
|---|---|---|---|
| `tests/Souq.IntegrationTests/AccessTokenValidationTests.cs` | `AccessTokenValidationTests`, `CancellingSessionValidator`, `FailingSessionValidator` | 4 | 0 |
| `tests/Souq.IntegrationTests/AuditTimestampsTests.cs` | `AuditTimestampsTests`, `SettableClock` | 1 | 0 |
| `tests/Souq.IntegrationTests/AuthConcurrencyTests.cs` | `AuthConcurrencyTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/AuthSessionTests.cs` | `AuthSessionTests` | 11 | 1 |
| `tests/Souq.IntegrationTests/AuthorizationBoundaryTests.cs` | `AuthorizationBoundaryTests` | 9 | 0 |
| `tests/Souq.IntegrationTests/AuthorizationMatrixTests.cs` | `AuthorizationMatrixTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/BackgroundSweepScopeTests.cs` | `BackgroundSweepScopeTests` | 2 | 0 |
| `tests/Souq.IntegrationTests/BasketConcurrencyTests.cs` | `BasketConcurrencyTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/BasketTests.cs` | `BasketTests` | 5 | 0 |
| `tests/Souq.IntegrationTests/BestSellingPerformanceTests.cs` | `BestSellingPerformanceTests`, `ScratchApp`, `SqlCapture`, `Sink` | 0 | 1 |
| `tests/Souq.IntegrationTests/CatalogSearchTests.cs` | `CatalogSearchTests` | 31 | 0 |
| `tests/Souq.IntegrationTests/CatalogTests.cs` | `CatalogTests`, `ProductCommandCounter` | 9 | 0 |
| `tests/Souq.IntegrationTests/CheckoutIdempotencyTests.cs` | `CheckoutIdempotencyTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/CommercialControlPlaneTests.cs` | `CommercialControlPlaneTests` | 7 | 0 |
| `tests/Souq.IntegrationTests/ConfigurationTests.cs` | `ConfigurationTests`, `ConfiguredFactory` | 10 | 6 |
| `tests/Souq.IntegrationTests/CookieSecurityTests.cs` | `CookieSecurityTests` | 4 | 0 |
| `tests/Souq.IntegrationTests/CouponRedemptionTests.cs` | `CouponRedemptionTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/CustomerAccountTests.cs` | `CustomerAccountTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/EnforcementDiagnosticsTests.cs` | `EnforcementDiagnosticsTests` | 3 | 3 |
| `tests/Souq.IntegrationTests/ErrorContractTests.cs` | `ErrorContractTests` | 8 | 1 |
| `tests/Souq.IntegrationTests/GlobalExceptionHandlerTests.cs` | `GlobalExceptionHandlerTests` | 4 | 1 |
| `tests/Souq.IntegrationTests/HealthCheckTests.cs` | `HealthCheckTests` | 7 | 0 |
| `tests/Souq.IntegrationTests/InventoryAndOrderTests.cs` | `InventoryAndOrderTests` | 11 | 0 |
| `tests/Souq.IntegrationTests/LastAdministratorConcurrencyTests.cs` | `LastAdministratorConcurrencyTests` | 2 | 0 |
| `tests/Souq.IntegrationTests/LocalFileStorageTests.cs` | `LocalFileStorageTests` | 2 | 2 |
| `tests/Souq.IntegrationTests/MigrationRehearsalTests.cs` | `MigrationRehearsalTests` | 2 | 0 |
| `tests/Souq.IntegrationTests/MigrationRollbackTests.cs` | `MigrationRollbackTests` | 1 | 0 |
| `tests/Souq.IntegrationTests/NotificationTests.cs` | `NotificationTests` | 11 | 0 |
| `tests/Souq.IntegrationTests/ObservabilityTests.cs` | `ObservabilityTests`, `AnonymousCaller` | 14 | 0 |
| `tests/Souq.IntegrationTests/OrderLifecycleTests.cs` | `OrderLifecycleTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/PaymentAdapterTests.cs` | `SecretProtectorTests`, `FakeGatewayTests` | 6 | 1 |
| `tests/Souq.IntegrationTests/PaymentDataRulesTests.cs` | `PaymentDataRulesTests` | 2 | 0 |
| `tests/Souq.IntegrationTests/PaymentsAndRefundsTests.cs` | `PaymentsAndRefundsTests` | 7 | 0 |
| `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs` | `PlatformAdministrationTests` | 9 | 0 |
| `tests/Souq.IntegrationTests/PlatformAuditViewerTests.cs` | `PlatformAuditViewerTests` | 5 | 0 |
| `tests/Souq.IntegrationTests/PlatformRevenueTests.cs` | `PlatformRevenueTests` | 5 | 0 |
| `tests/Souq.IntegrationTests/ProductOptionAdminTests.cs` | `ProductOptionAdminTests` | 9 | 0 |
| `tests/Souq.IntegrationTests/ProductVariantTests.cs` | `ProductVariantTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/ProvisioningBoundaryTests.cs` | `ProvisioningBoundaryTests` | 4 | 0 |
| `tests/Souq.IntegrationTests/QueryServiceTests.cs` | `QueryServiceTests` | 10 | 1 |
| `tests/Souq.IntegrationTests/RateLimitPolicyTests.cs` | `RateLimitPolicyTests` | 4 | 1 |
| `tests/Souq.IntegrationTests/ReadPathQueryBudgetTests.cs` | `ReadPathQueryBudgetTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/ReviewModerationTests.cs` | `ReviewModerationTests` | 5 | 0 |
| `tests/Souq.IntegrationTests/SearchAnalyticsTests.cs` | `SearchAnalyticsTests` | 12 | 0 |
| `tests/Souq.IntegrationTests/SecurityHeadersTests.cs` | `SecurityHeadersTests` | 6 | 2 |
| `tests/Souq.IntegrationTests/SeedSafetyTests.cs` | `SeedSafetyTests`, `FreshDatabaseFactory` | 4 | 0 |
| `tests/Souq.IntegrationTests/ShippingTests.cs` | `ShippingTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/StartupAndSecurityTests.cs` | `StartupAndSecurityTests`, `LoggerAdapter` | 7 | 1 |
| `tests/Souq.IntegrationTests/StoreAdministrationTests.cs` | `StoreAdministrationTests` | 7 | 0 |
| `tests/Souq.IntegrationTests/StoreBrandingPersistenceTests.cs` | `StoreBrandingPersistenceTests` | 4 | 0 |
| `tests/Souq.IntegrationTests/StoreDashboardTests.cs` | `StoreDashboardTests` | 20 | 1 |
| `tests/Souq.IntegrationTests/StorefrontVariantTests.cs` | `StorefrontVariantTests` | 7 | 0 |
| `tests/Souq.IntegrationTests/StripeAmountConverterTests.cs` | `StripeAmountConverterTests` | 2 | 2 |
| `tests/Souq.IntegrationTests/TenantIsolationTests.cs` | `TenantIsolationTests` | 13 | 0 |
| `tests/Souq.IntegrationTests/TenantQuotaTests.cs` | `TenantQuotaTests` | 7 | 0 |
| `tests/Souq.IntegrationTests/TenantResolutionMiddlewareTests.cs` | `TenantResolutionMiddlewareTests`, `FakeDirectory` | 3 | 3 |
| `tests/Souq.IntegrationTests/TenantResolutionTests.cs` | `TenantResolutionTests` | 8 | 0 |
| `tests/Souq.IntegrationTests/UploadSecurityTests.cs` | `UploadSecurityTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/WishlistTests.cs` | `WishlistTests` | 4 | 0 |

## Frontend (Vitest)

| File | Tests |
|---|---|
| `frontend/src/a11y.test.jsx` | 8 |
| `frontend/src/api/client.test.js` | 12 |
| `frontend/src/api/problem.test.js` | 6 |
| `frontend/src/api/query.test.js` | 4 |
| `frontend/src/app/QueryProvider.test.jsx` | 4 |
| `frontend/src/app/dateLocale.test.js` | 8 |
| `frontend/src/app/moduleInvariants.test.js` | 2 |
| `frontend/src/app/pageMetadata.test.js` | 11 |
| `frontend/src/app/robots.test.js` | 5 |
| `frontend/src/app/structuredData.test.js` | 8 |
| `frontend/src/app/tenantModel.test.js` | 8 |
| `frontend/src/app/tenantModel.themes.test.js` | 21 |
| `frontend/src/app/themeMode.test.js` | 9 |
| `frontend/src/components/ProtectedRoute.test.jsx` | 11 |
| `frontend/src/components/catalog/Catalog.test.jsx` | 7 |
| `frontend/src/components/common/ConfirmDialog.test.jsx` | 5 |
| `frontend/src/components/common/DataTable.test.jsx` | 4 |
| `frontend/src/components/common/Drawer.test.jsx` | 6 |
| `frontend/src/components/common/ErrorBoundary.test.jsx` | 5 |
| `frontend/src/components/common/FormField.test.jsx` | 3 |
| `frontend/src/components/common/Pagination.test.jsx` | 4 |
| `frontend/src/components/common/RowActionsMenu.test.jsx` | 3 |
| `frontend/src/components/common/Tabs.test.jsx` | 7 |
| `frontend/src/components/common/useConfirmAction.test.jsx` | 4 |
| `frontend/src/components/common/useDialog.test.jsx` | 8 |
| `frontend/src/components/layout/SearchBar.test.jsx` | 18 |
| `frontend/src/components/product/ProductCard.test.jsx` | 11 |
| `frontend/src/components/product/StarRating.test.jsx` | 7 |
| `frontend/src/components/reviews/ReviewForm.test.jsx` | 7 |
| `frontend/src/components/store/OpeningExperience.test.jsx` | 12 |
| `frontend/src/context/CartContext.loadFailure.test.jsx` | 3 |
| `frontend/src/features/account/addressForm.test.js` | 6 |
| `frontend/src/features/account/passwordForm.test.js` | 8 |
| `frontend/src/features/admin/categories/categoryForm.test.js` | 5 |
| `frontend/src/features/admin/coupons/couponForm.test.js` | 4 |
| `frontend/src/features/admin/customers/customerActions.test.js` | 4 |
| `frontend/src/features/admin/payments/paymentView.test.js` | 6 |
| `frontend/src/features/admin/products/productPayload.test.js` | 9 |
| `frontend/src/features/admin/products/productQuery.test.js` | 4 |
| `frontend/src/features/admin/products/variantModel.test.js` | 15 |
| `frontend/src/features/admin/reviews/reviewModeration.test.js` | 2 |
| `frontend/src/features/admin/search/insights.test.js` | 7 |
| `frontend/src/features/admin/search/synonymForm.test.js` | 7 |
| `frontend/src/features/admin/settings/settingsForm.test.js` | 19 |
| `frontend/src/features/admin/shipping/shippingForm.test.js` | 4 |
| `frontend/src/features/admin/staff/staffView.test.js` | 11 |
| `frontend/src/features/auth/safeRedirect.test.js` | 5 |
| `frontend/src/features/basket/basketModel.test.js` | 4 |
| `frontend/src/features/catalog/catalogText.test.js` | 5 |
| `frontend/src/features/catalog/productRouting.test.js` | 7 |
| `frontend/src/features/catalog/searchRouting.test.js` | 8 |
| `frontend/src/features/catalog/variantSelection.test.js` | 19 |
| `frontend/src/features/checkout/cardAppearance.test.js` | 5 |
| `frontend/src/features/checkout/shippingChoice.test.js` | 4 |
| `frontend/src/features/checkout/shippingOptions.test.js` | 4 |
| `frontend/src/features/notifications/notificationView.test.js` | 5 |
| `frontend/src/features/orders/orderView.test.js` | 3 |
| `frontend/src/features/platform/audit.test.js` | 9 |
| `frontend/src/features/platform/provisioning.test.js` | 22 |
| `frontend/src/features/reporting/businessHealth.test.js` | 22 |
| `frontend/src/features/reporting/chartScales.test.js` | 18 |
| `frontend/src/features/reporting/dashboardView.test.js` | 22 |
| `frontend/src/features/reviews/ratingSummary.test.js` | 3 |
| `frontend/src/features/statusTone.test.js` | 10 |
| `frontend/src/features/storefront/openingExperience.test.js` | 11 |
| `frontend/src/features/wishlist/wishlistModel.test.js` | 5 |
| `frontend/src/i18n/bidi.test.js` | 4 |
| `frontend/src/i18n/locales.test.js` | 2 |
| `frontend/src/i18n/translationKeys.test.js` | 8 |
| `frontend/src/pages/Cart.test.jsx` | 7 |
| `frontend/src/pages/Confirmation.test.jsx` | 7 |
| `frontend/src/pages/MyOrders.test.jsx` | 7 |
| `frontend/src/pages/OrderTracking.test.jsx` | 6 |
| `frontend/src/pages/ProductDetail.test.jsx` | 19 |
| `frontend/src/pages/account/AccountLayout.test.jsx` | 6 |
| `frontend/src/pages/account/AddressFormDrawer.test.jsx` | 5 |
| `frontend/src/pages/account/ChangePassword.test.jsx` | 7 |
| `frontend/src/pages/account/ProfileForm.test.jsx` | 5 |
| `frontend/src/pages/admin/BusinessOverview.test.jsx` | 10 |
| `frontend/src/pages/admin/Dashboard.test.jsx` | 11 |
| `frontend/src/pages/admin/Inventory.test.jsx` | 5 |
| `frontend/src/pages/admin/Orders.test.jsx` | 4 |
| `frontend/src/pages/admin/ProductVariants.test.jsx` | 7 |
| `frontend/src/pages/admin/Staff.test.jsx` | 8 |
| `frontend/src/pages/admin/StoreSettings.test.jsx` | 10 |
| `frontend/src/pages/checkout/Checkout.test.jsx` | 7 |
| `frontend/src/pages/checkout/stripeClient.test.js` | 3 |
| `frontend/src/pages/platform/Accounts.test.jsx` | 6 |
| `frontend/src/pages/platform/Audit.test.jsx` | 7 |
| `frontend/src/pages/platform/PlatformOverview.test.jsx` | 5 |
| `frontend/src/pages/platform/Provisioning.test.jsx` | 15 |
| `frontend/src/pages/storefrontPages.test.jsx` | 3 |
| `frontend/src/rtl.test.js` | 2 |
| `frontend/src/styles.darkTokens.test.js` | 3 |
| `frontend/src/whiteLabel.test.js` | 2 |

## Frontend (Playwright)

> Browser journeys, run by hand against a live stack — not in CI. How to run them: [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md).

| File | Journeys |
|---|---|
| `frontend/e2e/account-password.spec.js` | 3 |
| `frontend/e2e/admin-inventory.spec.js` | 3 |
| `frontend/e2e/admin-search-insights.spec.js` | 6 |
| `frontend/e2e/back-office.spec.js` | 6 |
| `frontend/e2e/checkout-coupon.spec.js` | 3 |
| `frontend/e2e/checkout-reliability.spec.js` | 4 |
| `frontend/e2e/cross-tenant-adversarial.spec.js` | 4 |
| `frontend/e2e/csp.spec.js` | 6 |
| `frontend/e2e/experience.spec.js` | 16 |
| `frontend/e2e/platform-provisioning.spec.js` | 9 |
| `frontend/e2e/product-variants.spec.js` | 5 |
| `frontend/e2e/responsive-storefront.spec.js` | 2 |
| `frontend/e2e/responsive.spec.js` | 13 |
| `frontend/e2e/search.spec.js` | 8 |
| `frontend/e2e/second-tenant.spec.js` | 6 |
| `frontend/e2e/session-tabs.spec.js` | 1 |
| `frontend/e2e/store-administration.spec.js` | 8 |
| `frontend/e2e/storefront-variants.spec.js` | 6 |
| `frontend/e2e/storefront.spec.js` | 17 |
