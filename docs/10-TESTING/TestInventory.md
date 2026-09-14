# Test inventory

> **Generated from the test sources** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). Do not edit by hand; regenerate with:
> `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
>
> Counts are test *methods* (`[Fact]`, `[Theory]`, Vitest `it`/`test`), not executed cases: a theory runs once per data row, so `dotnet test` reports more. What each suite is for: [TestingStrategy.md](TestingStrategy.md).

| Suite | Files | Facts | Theories |
|---|---|---|---|
| [`Souq.Domain.Tests`](#souqdomaintests) | 23 | 165 | 34 |
| [`Souq.Application.Tests`](#souqapplicationtests) | 39 | 272 | 11 |
| [`Souq.ArchitectureTests`](#souqarchitecturetests) | 8 | 36 | 3 |
| [`Souq.IntegrationTests`](#souqintegrationtests) | 32 | 170 | 15 |
| [frontend (Vitest)](#frontend-vitest) | 25 | 107 | — |

## Souq.Domain.Tests

| File | Classes | Facts | Theories |
|---|---|---|---|
| `tests/Souq.Domain.Tests/BasketTests.cs` | `BasketTests` | 8 | 1 |
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
| `tests/Souq.Domain.Tests/OrderTests.cs` | `OrderTests` | 23 | 1 |
| `tests/Souq.Domain.Tests/PaymentTests.cs` | `PaymentTests` | 5 | 3 |
| `tests/Souq.Domain.Tests/ProductTests.cs` | `ProductTests`, `CategoryTests` | 11 | 1 |
| `tests/Souq.Domain.Tests/RefreshTokenTests.cs` | `RefreshTokenTests` | 3 | 0 |
| `tests/Souq.Domain.Tests/ReviewTests.cs` | `ReviewTests` | 8 | 1 |
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
| `tests/Souq.Application.Tests/Auth/AuthHandlersTests.cs` | `AuthRig`, `RegisterHandlerTests`, `LoginHandlerTests`, `RefreshSessionHandlerTests`, `LogoutHandlerTests`, `ChangePasswordHandlerTests`, `ForgotPasswordHandlerTests`, `ResetPasswordHandlerTests`, `VerifyEmailHandlerTests` | 22 | 0 |
| `tests/Souq.Application.Tests/Baskets/BasketCheckoutTests.cs` | `BasketCheckoutTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Baskets/BasketHandlersTests.cs` | `BasketHandlersTests` | 9 | 0 |
| `tests/Souq.Application.Tests/Baskets/PricingServiceTests.cs` | `PricingServiceTests` | 8 | 0 |
| `tests/Souq.Application.Tests/Categories/CategoryHandlersTests.cs` | `CreateCategoryHandlerTests`, `UpdateCategoryHandlerTests`, `DeleteCategoryHandlerTests` | 13 | 0 |
| `tests/Souq.Application.Tests/Common/AccountsTests.cs` | `AccountsTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Common/AuditBehaviorTests.cs` | `AuditBehaviorTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Common/MediaFileInspectorTests.cs` | `MediaFileInspectorTests`, `NonSeekableStream` | 3 | 2 |
| `tests/Souq.Application.Tests/Common/PagingValidatorTests.cs` | `PagingValidatorTests` | 6 | 1 |
| `tests/Souq.Application.Tests/Common/TenantContextTests.cs` | `TenantContextTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Common/UseCaseLoggingBehaviorTests.cs` | `UseCaseLoggingBehaviorTests`, `SteppingClock`, `ListLoggerFactory`, `ListLogger` | 3 | 0 |
| `tests/Souq.Application.Tests/Coupons/ApplyCouponHandlerTests.cs` | `ApplyCouponHandlerTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Coupons/CouponHandlersTests.cs` | `CouponHandlersTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Coupons/CouponRedemptionsTests.cs` | `CouponRedemptionsTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Customers/CustomerHandlersTests.cs` | `CustomerAccountHandlersTests`, `CreateOrderCustomerRulesTests`, `AddressInputTestExtensions` | 9 | 0 |
| `tests/Souq.Application.Tests/Inventory/InventoryCommandsTests.cs` | `InventoryCommandsTests` | 4 | 0 |
| `tests/Souq.Application.Tests/Inventory/InventoryReservationsTests.cs` | `InventoryReservationsTests` | 9 | 0 |
| `tests/Souq.Application.Tests/Notifications/NotificationHandlersTests.cs` | `OutboxPolicyTests`, `IdentityEmailHandlersTests`, `OrderNotificationHandlersTests`, `NotificationUseCasesTests` | 14 | 1 |
| `tests/Souq.Application.Tests/Orders/CancelMyOrderHandlerTests.cs` | `CancelMyOrderHandlerTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Orders/ConfirmOrderPaymentHandlerTests.cs` | `ConfirmOrderPaymentHandlerTests` | 12 | 0 |
| `tests/Souq.Application.Tests/Orders/CreateOrderHandlerTests.cs` | `CreateOrderHandlerTests` | 11 | 0 |
| `tests/Souq.Application.Tests/Orders/ExpireStaleCheckoutsHandlerTests.cs` | `ExpireStaleCheckoutsHandlerTests` | 6 | 0 |
| `tests/Souq.Application.Tests/Orders/GetOrderByIdHandlerTests.cs` | `GetOrderByIdHandlerTests` | 7 | 1 |
| `tests/Souq.Application.Tests/Orders/ProcessPaymentWebhookHandlerTests.cs` | `ProcessPaymentWebhookHandlerTests`, `ApplyPaymentEventHandlerTests` | 6 | 2 |
| `tests/Souq.Application.Tests/Orders/UpdateOrderStatusHandlerTests.cs` | `UpdateOrderStatusHandlerTests` | 9 | 2 |
| `tests/Souq.Application.Tests/Payments/OrderPaymentsTests.cs` | `OrderPaymentsTests` | 10 | 0 |
| `tests/Souq.Application.Tests/Platform/TenantAdministrationTests.cs` | `TenantAdministrationTests` | 5 | 0 |
| `tests/Souq.Application.Tests/Products/GetProductsHandlerTests.cs` | `GetProductsHandlerTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Products/GetRelatedProductsHandlerTests.cs` | `GetRelatedProductsHandlerTests` | 3 | 0 |
| `tests/Souq.Application.Tests/Products/ProductHandlersTests.cs` | `CreateProductHandlerTests`, `UpdateProductHandlerTests`, `ProductLifecycleHandlerTests`, `GetProductByIdHandlerTests` | 17 | 0 |
| `tests/Souq.Application.Tests/Products/UploadProductMediaHandlerTests.cs` | `UploadProductMediaHandlerTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Reviews/CreateReviewHandlerTests.cs` | `CreateReviewHandlerTests` | 7 | 0 |
| `tests/Souq.Application.Tests/Reviews/ReviewModerationHandlersTests.cs` | `ReviewModerationHandlersTests` | 6 | 0 |
| `tests/Souq.Application.Tests/Security/RolePermissionsTests.cs` | `RolePermissionsTests` | 8 | 0 |
| `tests/Souq.Application.Tests/Shipping/ShippingMethodHandlersTests.cs` | `ShippingMethodHandlersTests` | 3 | 0 |
| `tests/Souq.Application.Tests/Shipping/StoreShippingRatesTests.cs` | `StoreShippingRatesTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Stores/ReviewSettingsHandlersTests.cs` | `ReviewSettingsHandlersTests` | 2 | 0 |
| `tests/Souq.Application.Tests/Stores/StorePaymentAccountEditorTests.cs` | `StorePaymentAccountEditorTests` | 5 | 1 |
| `tests/Souq.Application.Tests/Wishlist/WishlistHandlersTests.cs` | `WishlistHandlersTests` | 9 | 1 |

## Souq.ArchitectureTests

| File | Classes | Facts | Theories |
|---|---|---|---|
| `tests/Souq.ArchitectureTests/ClockRuleTests.cs` | `ClockRuleTests` | 0 | 1 |
| `tests/Souq.ArchitectureTests/DependencyRuleTests.cs` | `DependencyRuleTests` | 8 | 0 |
| `tests/Souq.ArchitectureTests/DocumentationTests.cs` | `DocumentationTests` | 4 | 0 |
| `tests/Souq.ArchitectureTests/EndpointRuleTests.cs` | `EndpointRuleTests` | 3 | 0 |
| `tests/Souq.ArchitectureTests/GeneratedDocsTests.cs` | `GeneratedDocsTests` | 5 | 1 |
| `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs` | `ModuleAndContractRuleTests` | 8 | 1 |
| `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` | `TenancyRuleTests` | 7 | 0 |
| `tests/Souq.ArchitectureTests/WhiteLabelSourceTests.cs` | `WhiteLabelSourceTests` | 1 | 0 |

## Souq.IntegrationTests

| File | Classes | Facts | Theories |
|---|---|---|---|
| `tests/Souq.IntegrationTests/AuditTimestampsTests.cs` | `AuditTimestampsTests`, `SettableClock` | 1 | 0 |
| `tests/Souq.IntegrationTests/AuthSessionTests.cs` | `AuthSessionTests` | 10 | 0 |
| `tests/Souq.IntegrationTests/AuthorizationBoundaryTests.cs` | `AuthorizationBoundaryTests` | 7 | 0 |
| `tests/Souq.IntegrationTests/AuthorizationMatrixTests.cs` | `AuthorizationMatrixTests` | 2 | 0 |
| `tests/Souq.IntegrationTests/BasketTests.cs` | `BasketTests` | 5 | 0 |
| `tests/Souq.IntegrationTests/CatalogTests.cs` | `CatalogTests`, `ProductCommandCounter` | 9 | 0 |
| `tests/Souq.IntegrationTests/ConfigurationTests.cs` | `ConfigurationTests`, `ConfiguredFactory` | 5 | 4 |
| `tests/Souq.IntegrationTests/CouponRedemptionTests.cs` | `CouponRedemptionTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/CustomerAccountTests.cs` | `CustomerAccountTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/ErrorContractTests.cs` | `ErrorContractTests` | 8 | 1 |
| `tests/Souq.IntegrationTests/GlobalExceptionHandlerTests.cs` | `GlobalExceptionHandlerTests` | 4 | 1 |
| `tests/Souq.IntegrationTests/InventoryAndOrderTests.cs` | `InventoryAndOrderTests` | 9 | 0 |
| `tests/Souq.IntegrationTests/LocalFileStorageTests.cs` | `LocalFileStorageTests` | 2 | 2 |
| `tests/Souq.IntegrationTests/MigrationRehearsalTests.cs` | `MigrationRehearsalTests` | 1 | 0 |
| `tests/Souq.IntegrationTests/NotificationTests.cs` | `NotificationTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/ObservabilityTests.cs` | `ObservabilityTests` | 5 | 0 |
| `tests/Souq.IntegrationTests/OrderLifecycleTests.cs` | `OrderLifecycleTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/PaymentAdapterTests.cs` | `SecretProtectorTests`, `FakeGatewayTests` | 6 | 1 |
| `tests/Souq.IntegrationTests/PaymentDataRulesTests.cs` | `PaymentDataRulesTests` | 2 | 0 |
| `tests/Souq.IntegrationTests/PaymentsAndRefundsTests.cs` | `PaymentsAndRefundsTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs` | `PlatformAdministrationTests` | 9 | 0 |
| `tests/Souq.IntegrationTests/QueryServiceTests.cs` | `QueryServiceTests` | 10 | 1 |
| `tests/Souq.IntegrationTests/ReviewModerationTests.cs` | `ReviewModerationTests` | 4 | 0 |
| `tests/Souq.IntegrationTests/ShippingTests.cs` | `ShippingTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/StartupAndSecurityTests.cs` | `StartupAndSecurityTests`, `LoggerAdapter` | 7 | 1 |
| `tests/Souq.IntegrationTests/StoreAdministrationTests.cs` | `StoreAdministrationTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/StripeAmountConverterTests.cs` | `StripeAmountConverterTests` | 0 | 1 |
| `tests/Souq.IntegrationTests/TenantIsolationTests.cs` | `TenantIsolationTests` | 12 | 0 |
| `tests/Souq.IntegrationTests/TenantResolutionMiddlewareTests.cs` | `TenantResolutionMiddlewareTests`, `FakeDirectory` | 3 | 3 |
| `tests/Souq.IntegrationTests/TenantResolutionTests.cs` | `TenantResolutionTests` | 6 | 0 |
| `tests/Souq.IntegrationTests/UploadSecurityTests.cs` | `UploadSecurityTests` | 3 | 0 |
| `tests/Souq.IntegrationTests/WishlistTests.cs` | `WishlistTests` | 4 | 0 |

## Frontend (Vitest)

| File | Tests |
|---|---|
| `frontend/src/api/client.test.js` | 6 |
| `frontend/src/api/problem.test.js` | 6 |
| `frontend/src/api/query.test.js` | 4 |
| `frontend/src/app/tenantModel.test.js` | 7 |
| `frontend/src/features/account/addressForm.test.js` | 4 |
| `frontend/src/features/admin/categories/categoryForm.test.js` | 5 |
| `frontend/src/features/admin/coupons/couponForm.test.js` | 4 |
| `frontend/src/features/admin/customers/customerActions.test.js` | 4 |
| `frontend/src/features/admin/payments/paymentView.test.js` | 6 |
| `frontend/src/features/admin/products/productPayload.test.js` | 8 |
| `frontend/src/features/admin/products/productQuery.test.js` | 4 |
| `frontend/src/features/admin/reviews/reviewModeration.test.js` | 2 |
| `frontend/src/features/admin/shipping/shippingForm.test.js` | 4 |
| `frontend/src/features/auth/safeRedirect.test.js` | 5 |
| `frontend/src/features/basket/basketModel.test.js` | 4 |
| `frontend/src/features/catalog/catalogText.test.js` | 5 |
| `frontend/src/features/checkout/shippingChoice.test.js` | 4 |
| `frontend/src/features/checkout/shippingOptions.test.js` | 4 |
| `frontend/src/features/notifications/notificationView.test.js` | 4 |
| `frontend/src/features/orders/orderView.test.js` | 3 |
| `frontend/src/features/reviews/ratingSummary.test.js` | 3 |
| `frontend/src/features/wishlist/wishlistModel.test.js` | 5 |
| `frontend/src/i18n/locales.test.js` | 2 |
| `frontend/src/pages/checkout/stripeClient.test.js` | 3 |
| `frontend/src/whiteLabel.test.js` | 1 |
