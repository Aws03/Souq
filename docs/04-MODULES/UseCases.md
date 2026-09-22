# Use-case catalog

> **Generated from the compiled code** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). Do not edit by hand; regenerate with:
> `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
>
> Every MediatR command and query per module, with its handler, its validator, whether it is audited, and the endpoints that send it; then each module's public contracts (the interfaces other modules may call) and what implements them. Module docs explain the *why*: [Modules.md](Modules.md). Endpoints: [Endpoints.md](../05-API/Endpoints.md).

| Module | Feature folders | Commands | Queries | Contracts |
|---|---|---|---|---|
| [Platform](#platform) | `src/Souq.Application/Features/Platform`, `src/Souq.Application/Features/Stores` | 15 | 11 | 0 |
| [Identity](#identity) | `src/Souq.Application/Features/Auth`, `src/Souq.Application/Features/Staff` | 11 | 2 | 2 |
| [Catalog](#catalog) | `src/Souq.Application/Features/Products`, `src/Souq.Application/Features/Categories` | 20 | 11 | 3 |
| [Inventory](#inventory) | `src/Souq.Application/Features/Inventory` | 4 | 4 | 2 |
| [Customers](#customers) | `src/Souq.Application/Features/Customers` | 8 | 6 | 0 |
| [Shopping](#shopping) | `src/Souq.Application/Features/Baskets`, `src/Souq.Application/Features/Wishlist` | 10 | 2 | 2 |
| [Ordering](#ordering) | `src/Souq.Application/Features/Orders` | 7 | 4 | 0 |
| [Payments](#payments) | `src/Souq.Application/Features/Payments` | 4 | 2 | 3 |
| [Promotions](#promotions) | `src/Souq.Application/Features/Coupons` | 3 | 2 | 1 |
| [Shipping](#shipping) | `src/Souq.Application/Features/Shipping` | 3 | 1 | 1 |
| [Reviews](#reviews) | `src/Souq.Application/Features/Reviews` | 3 | 2 | 0 |
| [Notifications](#notifications) | `src/Souq.Application/Features/Notifications` | 2 | 2 | 0 |
| [Reporting](#reporting) | `src/Souq.Application/Features/Reporting`, `src/Souq.Application/Features/Analytics` | 2 | 3 | 4 |
| [Billing](#billing) | `src/Souq.Application/Features/Billing`, `src/Souq.Application/Features/Subscriptions` | 20 | 12 | 2 |
| [Tax](#tax) | `src/Souq.Application/Features/Tax` | 6 | 3 | 1 |

## Platform

Module document: [Platform/README.md](Platform/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `ChangeTenantDomainCommand` | command | `ChangeTenantDomainHandler` | `ChangeTenantDomainValidator` | yes | `POST /api/platform/tenants/{id:int}/domains`<br>`DELETE /api/platform/tenants/{id:int}/domains/{host}`<br>`POST /api/platform/tenants/{id:int}/domains/{host}/primary`<br>`POST /api/platform/tenants/{id:int}/domains/{host}/verify` |
| `ChangeTenantStatusCommand` | command | `ChangeTenantStatusHandler` | `ChangeTenantStatusValidator` | yes | `POST /api/platform/tenants/{id:int}/status` |
| `CreateTenantCommand` | command | `CreateTenantHandler` | `CreateTenantValidator` | yes | `POST /api/platform/tenants` |
| `InvitePlatformUserCommand` | command | `InvitePlatformUserHandler` | `InvitePlatformUserValidator` | yes | `POST /api/platform/users` |
| `InviteTenantAdminCommand` | command | `InviteTenantAdminHandler` | `InviteTenantAdminValidator` | yes | `POST /api/platform/tenants/{id:int}/admins` |
| `RemoveTenantPaymentAccountCommand` | command | `RemoveTenantPaymentAccountHandler` | — | yes | `DELETE /api/platform/tenants/{id:int}/payments` |
| `SetPlatformUserStatusCommand` | command | `SetPlatformUserStatusHandler` | — | yes | `POST /api/platform/users/{id:int}/status` |
| `SetTenantModulesCommand` | command | `SetTenantModulesHandler` | `SetTenantModulesValidator` | yes | `PUT /api/platform/tenants/{id:int}/modules` |
| `UpdateReviewSettingsCommand` | command | `UpdateReviewSettingsHandler` | — | yes | `PUT /api/admin/reviews/settings` |
| `UpdateStoreSettingsCommand` | command | `UpdateStoreSettingsHandler` | `UpdateStoreSettingsValidator` | yes | `PUT /api/admin/store/settings` |
| `UpdateTenantCommand` | command | `UpdateTenantHandler` | `UpdateTenantValidator` | yes | `PUT /api/platform/tenants/{id:int}` |
| `UpdateTenantPaymentAccountCommand` | command | `UpdateTenantPaymentAccountHandler` | `UpdateTenantPaymentAccountValidator` | yes | `PUT /api/platform/tenants/{id:int}/payments` |
| `UpdateTenantSettingsCommand` | command | `UpdateTenantSettingsHandler` | `UpdateTenantSettingsValidator` | yes | `PUT /api/platform/tenants/{id:int}/settings` |
| `UploadStoreBrandingCommand` | command | `UploadStoreBrandingHandler` | — | yes | no endpoint (sent internally) |
| `UploadTenantBrandingCommand` | command | `UploadTenantBrandingHandler` | — | yes | `POST /api/platform/tenants/{id:int}/branding/{asset}` |
| `GetProvisioningOptionsQuery` | query | `GetProvisioningOptionsHandler` | — | yes | `GET /api/platform/tenants/options` |
| `GetReviewSettingsQuery` | query | `GetReviewSettingsHandler` | — | — | `GET /api/admin/reviews/settings` |
| `GetStoreSettingsOptionsQuery` | query | `GetStoreSettingsOptionsHandler` | — | — | `GET /api/admin/store/settings/options` |
| `GetStoreSettingsQuery` | query | `GetStoreSettingsHandler` | — | — | `GET /api/admin/store/settings` |
| `GetStorefrontConfigQuery` | query | `GetStorefrontConfigHandler` | — | — | `GET /api/storefront/config` |
| `GetTenantPaymentAccountQuery` | query | `GetTenantPaymentAccountHandler` | — | yes | `GET /api/platform/tenants/{id:int}/payments` |
| `GetTenantQuery` | query | `GetTenantHandler` | — | yes | `GET /api/platform/tenants/{id:int}` |
| `ListAuditEntriesQuery` | query | `ListAuditEntriesHandler` | `ListAuditEntriesQueryValidator` | yes | `GET /api/platform/audit` |
| `ListPlatformUsersQuery` | query | `ListPlatformUsersHandler` | `ListPlatformUsersQueryValidator` | yes | `GET /api/platform/users` |
| `ListTenantAccountsQuery` | query | `ListTenantAccountsHandler` | `ListTenantAccountsQueryValidator` | yes | `GET /api/platform/tenants/{id:int}/accounts` |
| `ListTenantsQuery` | query | `ListTenantsHandler` | `ListTenantsQueryValidator` | yes | `GET /api/platform/tenants` |

## Identity

Module document: [Identity/README.md](Identity/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `ChangePasswordCommand` | command | `ChangePasswordHandler` | `ChangePasswordValidator` | — | `POST /api/auth/change-password` |
| `ForgotPasswordCommand` | command | `ForgotPasswordHandler` | `ForgotPasswordValidator` | — | `POST /api/auth/forgot-password` |
| `InviteStaffCommand` | command | `InviteStaffHandler` | `InviteStaffValidator` | yes | `POST /api/admin/staff` |
| `LoginCommand` | command | `LoginHandler` | `LoginValidator` | — | `POST /api/auth/login` |
| `LogoutCommand` | command | `LogoutHandler` | — | — | `POST /api/auth/logout` |
| `RefreshSessionCommand` | command | `RefreshSessionHandler` | — | — | `POST /api/auth/refresh` |
| `RegisterCommand` | command | `RegisterHandler` | `RegisterValidator` | — | `POST /api/auth/register` |
| `ResendVerificationCommand` | command | `ResendVerificationHandler` | — | — | `POST /api/auth/resend-verification` |
| `ResetPasswordCommand` | command | `ResetPasswordHandler` | `ResetPasswordValidator` | — | `POST /api/auth/reset-password` |
| `SetStaffStatusCommand` | command | `SetStaffStatusHandler` | — | yes | `POST /api/admin/staff/{id:int}/status` |
| `VerifyEmailCommand` | command | `VerifyEmailHandler` | `VerifyEmailValidator` | — | `POST /api/auth/verify-email` |
| `GetCurrentUserQuery` | query | `GetCurrentUserHandler` | — | — | `GET /api/auth/me` |
| `ListStaffQuery` | query | `ListStaffHandler` | `ListStaffQueryValidator` | — | `GET /api/admin/staff` |

| Public contract | Implemented by |
|---|---|
| `IAccountLifecycle` | `AccountLifecycle` |
| `IAccountProfiles` | `CustomerAccountProfiles` |

## Catalog

Module document: [Catalog/README.md](Catalog/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `ChangeProductStatusCommand` | command | `ChangeProductStatusHandler` | `ChangeProductStatusValidator` | yes | `PUT /api/admin/products/{id:int}/status` |
| `CreateCategoryCommand` | command | `CreateCategoryHandler` | `CreateCategoryValidator` | yes | `POST /api/categories` |
| `CreateProductCommand` | command | `CreateProductHandler` | `CreateProductValidator` | yes | `POST /api/products` |
| `CreateProductVariantsCommand` | command | `CreateProductVariantsHandler` | `CreateProductVariantsValidator` | yes | `POST /api/admin/products/{id:int}/variants` |
| `CreateSearchSynonymCommand` | command | `CreateSearchSynonymHandler` | `CreateSearchSynonymValidator` | yes | `POST /api/admin/search-synonyms` |
| `DeleteCategoryCommand` | command | `DeleteCategoryHandler` | `DeleteCategoryValidator` | yes | `DELETE /api/categories/{id:int}` |
| `DeleteProductCommand` | command | `DeleteProductHandler` | `DeleteProductValidator` | yes | `DELETE /api/products/{id:int}` |
| `DeleteSearchSynonymCommand` | command | `DeleteSearchSynonymHandler` | `DeleteSearchSynonymValidator` | yes | `DELETE /api/admin/search-synonyms/{id:int}` |
| `PurgeSearchLogCommand` | command | `PurgeSearchLogHandler` | — | — | no endpoint (sent internally) |
| `RemoveProductImageCommand` | command | `RemoveProductImageHandler` | — | yes | `DELETE /api/admin/products/{id:int}/images/{imageId:int}` |
| `ReorderProductImagesCommand` | command | `ReorderProductImagesHandler` | `ReorderProductImagesValidator` | yes | `PUT /api/admin/products/{id:int}/images/order` |
| `SetDefaultProductVariantCommand` | command | `SetDefaultProductVariantHandler` | `SetDefaultProductVariantValidator` | yes | `PUT /api/admin/products/{id:int}/variants/{variantId:int}/default` |
| `SetProductOptionsCommand` | command | `SetProductOptionsHandler` | `SetProductOptionsValidator` | yes | `PUT /api/admin/products/{id:int}/options` |
| `SetProductVariantStatusCommand` | command | `SetProductVariantStatusHandler` | `SetProductVariantStatusValidator` | yes | `PUT /api/admin/products/{id:int}/variants/{variantId:int}/status` |
| `UpdateCategoryCommand` | command | `UpdateCategoryHandler` | `UpdateCategoryValidator` | yes | `PUT /api/categories/{id:int}` |
| `UpdateProductCommand` | command | `UpdateProductHandler` | `UpdateProductValidator` | yes | `PUT /api/products/{id:int}` |
| `UpdateProductVariantCommand` | command | `UpdateProductVariantHandler` | `UpdateProductVariantValidator` | yes | `PUT /api/admin/products/{id:int}/variants/{variantId:int}` |
| `UpdateSearchSynonymCommand` | command | `UpdateSearchSynonymHandler` | `UpdateSearchSynonymValidator` | yes | `PUT /api/admin/search-synonyms/{id:int}` |
| `UploadProductImageCommand` | command | `UploadProductImageHandler` | — | yes | `POST /api/products/{id:int}/image` |
| `UploadProductVideoCommand` | command | `UploadProductVideoHandler` | — | yes | `POST /api/products/{id:int}/video` |
| `GetAdminProductQuery` | query | `GetAdminProductHandler` | — | — | `GET /api/admin/products/{id:int}` |
| `GetCategoriesQuery` | query | `GetCategoriesHandler` | — | — | `GET /api/categories` |
| `GetProductByIdQuery` | query | `GetProductByIdHandler` | — | — | `GET /api/products/{id:int}` |
| `GetProductBySlugQuery` | query | `GetProductByIdHandler` | `GetProductBySlugQueryValidator` | — | `GET /api/products/by-slug/{slug}` |
| `GetProductsQuery` | query | `GetProductsHandler` | `GetProductsQueryValidator` | — | `GET /api/products` |
| `GetRelatedProductsQuery` | query | `GetRelatedProductsHandler` | `GetRelatedProductsQueryValidator` | — | `GET /api/products/{id:int}/related` |
| `GetSearchSuggestionsQuery` | query | `GetSearchSuggestionsHandler` | `GetSearchSuggestionsValidator` | — | `GET /api/products/suggestions` |
| `ListAdminCategoriesQuery` | query | `GetCategoriesHandler` | — | — | `GET /api/admin/categories` |
| `ListAdminProductsQuery` | query | `ListAdminProductsHandler` | `ListAdminProductsQueryValidator` | — | `GET /api/admin/products` |
| `ListSearchSynonymsQuery` | query | `ListSearchSynonymsHandler` | — | — | `GET /api/admin/search-synonyms` |
| `SearchInsightsQuery` | query | `SearchInsightsHandler` | `SearchInsightsQueryValidator` | — | `GET /api/admin/search-synonyms/insights` |

| Public contract | Implemented by |
|---|---|
| `ISearchLog` | `SearchLogBuffer` |
| `ISearchLogRetention` | `SearchLogRetention` |
| `IVariantStockInitializer` | `VariantStockInitializer` |

## Inventory

Module document: [Inventory/README.md](Inventory/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `AdjustStockCommand` | command | `AdjustStockHandler` | `AdjustStockValidator` | yes | `POST /api/admin/inventory/{productId:int}/adjustments` |
| `AdjustVariantStockCommand` | command | `AdjustVariantStockHandler` | `AdjustVariantStockValidator` | yes | `POST /api/admin/inventory/variants/{variantId:int}/adjustments` |
| `SetLowStockThresholdCommand` | command | `SetLowStockThresholdHandler` | `SetLowStockThresholdValidator` | yes | `PUT /api/admin/inventory/{productId:int}/threshold` |
| `SetVariantLowStockThresholdCommand` | command | `SetVariantLowStockThresholdHandler` | `SetVariantLowStockThresholdValidator` | yes | `PUT /api/admin/inventory/variants/{variantId:int}/threshold` |
| `GetInventoryQuery` | query | `GetInventoryHandler` | `GetInventoryQueryValidator` | — | `GET /api/admin/inventory` |
| `GetLowStockQuery` | query | `GetLowStockHandler` | `GetLowStockQueryValidator` | — | `GET /api/admin/inventory/low-stock` |
| `GetStockMovementsQuery` | query | `GetStockMovementsHandler` | `GetStockMovementsQueryValidator` | — | `GET /api/admin/inventory/{productId:int}/movements` |
| `GetVariantStockMovementsQuery` | query | `GetVariantStockMovementsHandler` | `GetVariantStockMovementsQueryValidator` | — | `GET /api/admin/inventory/variants/{variantId:int}/movements` |

| Public contract | Implemented by |
|---|---|
| `IInventoryReservations` | `InventoryReservations` |
| `IStockAvailability` | `InventoryReservations` |

## Customers

Module document: [Customers/README.md](Customers/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `AddMyAddressCommand` | command | `AddMyAddressHandler` | `AddMyAddressValidator` | — | `POST /api/account/addresses` |
| `EraseCustomerCommand` | command | `EraseCustomerHandler` | — | yes | `POST /api/admin/customers/{id:int}/erase` |
| `EraseMyAccountCommand` | command | `EraseMyAccountHandler` | `EraseMyAccountValidator` | yes | `POST /api/account/erase` |
| `RemoveMyAddressCommand` | command | `RemoveMyAddressHandler` | — | — | `DELETE /api/account/addresses/{id:int}` |
| `SetCustomerStatusCommand` | command | `SetCustomerStatusHandler` | `SetCustomerStatusValidator` | yes | `PUT /api/admin/customers/{id:int}/status` |
| `SetMyDefaultAddressCommand` | command | `SetMyDefaultAddressHandler` | `SetMyDefaultAddressValidator` | — | `PUT /api/account/addresses/{id:int}/default-billing`<br>`PUT /api/account/addresses/{id:int}/default-shipping` |
| `UpdateMyAddressCommand` | command | `UpdateMyAddressHandler` | `UpdateMyAddressValidator` | — | `PUT /api/account/addresses/{id:int}` |
| `UpdateMyProfileCommand` | command | `UpdateMyProfileHandler` | `UpdateMyProfileValidator` | — | `PUT /api/account/profile` |
| `ExportCustomerDataQuery` | query | `ExportCustomerDataHandler` | — | yes | `GET /api/admin/customers/{id:int}/export` |
| `ExportMyDataQuery` | query | `ExportMyDataHandler` | — | yes | `GET /api/account/export` |
| `GetCustomerQuery` | query | `GetCustomerHandler` | — | yes | `GET /api/admin/customers/{id:int}` |
| `GetMyProfileQuery` | query | `GetMyProfileHandler` | — | — | `GET /api/account/profile` |
| `ListCustomersQuery` | query | `ListCustomersHandler` | `ListCustomersQueryValidator` | yes | `GET /api/admin/customers` |
| `ListMyAddressesQuery` | query | `ListMyAddressesHandler` | — | — | `GET /api/account/addresses` |

## Shopping

Module document: [Shopping/README.md](Shopping/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `AddBasketItemCommand` | command | `AddBasketItemHandler` | `AddBasketItemValidator` | — | `POST /api/basket/items` |
| `AddToWishlistCommand` | command | `AddToWishlistHandler` | — | — | `PUT /api/wishlist/{productId:int}` |
| `ClearBasketCommand` | command | `ClearBasketHandler` | — | — | `DELETE /api/basket` |
| `MergeWishlistCommand` | command | `MergeWishlistHandler` | `MergeWishlistValidator` | — | `POST /api/wishlist/merge` |
| `PurgeExpiredBasketsCommand` | command | `PurgeExpiredBasketsHandler` | — | — | no endpoint (sent internally) |
| `RemoveBasketItemCommand` | command | `RemoveBasketItemHandler` | — | — | `DELETE /api/basket/items/{productId:int}` |
| `RemoveBasketLineCommand` | command | `RemoveBasketLineHandler` | — | — | `DELETE /api/basket/items/variants/{variantId:int}` |
| `RemoveFromWishlistCommand` | command | `RemoveFromWishlistHandler` | — | — | `DELETE /api/wishlist/{productId:int}` |
| `SetBasketItemQuantityCommand` | command | `SetBasketItemQuantityHandler` | `SetBasketItemQuantityValidator` | — | `PUT /api/basket/items/{productId:int}` |
| `SetBasketLineQuantityCommand` | command | `SetBasketLineQuantityHandler` | `SetBasketLineQuantityValidator` | — | `PUT /api/basket/items/variants/{variantId:int}` |
| `GetBasketQuery` | query | `GetBasketHandler` | `GetBasketValidator` | — | `GET /api/basket`<br>`GET /api/basket/quote` |
| `GetWishlistQuery` | query | `GetWishlistHandler` | — | — | `GET /api/wishlist` |

| Public contract | Implemented by |
|---|---|
| `IBasketCheckout` | `BasketCheckout` |
| `IPricing` | `PricingService` |

## Ordering

Module document: [Ordering/README.md](Ordering/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `ApplyPaymentEventCommand` | command | `ApplyPaymentEventHandler` | — | — | no endpoint (sent internally) |
| `CancelMyOrderCommand` | command | `CancelMyOrderHandler` | `CancelMyOrderValidator` | — | `POST /api/orders/{id:int}/cancel` |
| `ConfirmOrderPaymentCommand` | command | `ConfirmOrderPaymentHandler` | — | — | `POST /api/orders/{id:int}/confirm-payment` |
| `CreateOrderCommand` | command | `CreateOrderHandler` | `CreateOrderValidator` | — | `POST /api/orders` |
| `ExpireStaleCheckoutsCommand` | command | `ExpireStaleCheckoutsHandler` | — | — | no endpoint (sent internally) |
| `ProcessPaymentWebhookCommand` | command | `ProcessPaymentWebhookHandler` | — | — | `POST /api/payments/webhook` |
| `UpdateOrderStatusCommand` | command | `UpdateOrderStatusHandler` | `UpdateOrderStatusValidator` | — | `PUT /api/orders/{id:int}/status` |
| `GetMyOrdersQuery` | query | `GetMyOrdersHandler` | `GetMyOrdersQueryValidator` | — | `GET /api/orders/mine` |
| `GetOrderByIdQuery` | query | `GetOrderByIdHandler` | — | — | `GET /api/orders/{id:int}` |
| `GetOrderTrackingQuery` | query | `GetOrderTrackingHandler` | — | — | `GET /api/orders/track/{token}` |
| `GetOrdersQuery` | query | `GetOrdersHandler` | `GetOrdersQueryValidator` | — | `GET /api/orders` |

## Payments

Module document: [Payments/README.md](Payments/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `RefundOrderCommand` | command | `RefundOrderHandler` | `RefundOrderValidator` | yes | `POST /api/orders/{id:int}/refunds` |
| `RemoveStorePaymentAccountCommand` | command | `RemoveStorePaymentAccountHandler` | — | yes | `DELETE /api/admin/store/payments` |
| `RetryRefundCommand` | command | `RetryRefundHandler` | — | yes | `POST /api/orders/{id:int}/refunds/{refundId:int}/retry` |
| `UpdateStorePaymentAccountCommand` | command | `UpdateStorePaymentAccountHandler` | `UpdateStorePaymentAccountValidator` | yes | `PUT /api/admin/store/payments` |
| `GetPaymentConfigQuery` | query | `GetPaymentConfigHandler` | — | — | `GET /api/payments/config` |
| `GetStorePaymentAccountQuery` | query | `GetStorePaymentAccountHandler` | — | — | `GET /api/admin/store/payments` |

| Public contract | Implemented by |
|---|---|
| `IOrderPayments` | `OrderPayments` |
| `IPaymentQueries` | `PaymentQueries` |
| `IStorePaymentAccountEditor` | `StorePaymentAccountEditor` |

## Promotions

Module document: [Promotions/README.md](Promotions/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `CreateCouponCommand` | command | `CreateCouponHandler` | `CreateCouponValidator` | — | `POST /api/coupons` |
| `DeleteCouponCommand` | command | `DeleteCouponHandler` | `DeleteCouponValidator` | — | `DELETE /api/coupons/{id:int}` |
| `UpdateCouponCommand` | command | `UpdateCouponHandler` | `UpdateCouponValidator` | — | `PUT /api/coupons/{id:int}` |
| `GetCouponRedemptionsQuery` | query | `GetCouponRedemptionsHandler` | `GetCouponRedemptionsQueryValidator` | — | `GET /api/coupons/{id:int}/redemptions` |
| `GetCouponsQuery` | query | `GetCouponsHandler` | `GetCouponsQueryValidator` | — | `GET /api/coupons` |

| Public contract | Implemented by |
|---|---|
| `ICouponRedemptions` | `CouponRedemptions` |

## Shipping

Module document: [Shipping/README.md](Shipping/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `CreateShippingMethodCommand` | command | `CreateShippingMethodHandler` | `CreateShippingMethodValidator` | — | `POST /api/admin/shipping-methods` |
| `DeleteShippingMethodCommand` | command | `DeleteShippingMethodHandler` | — | — | `DELETE /api/admin/shipping-methods/{id:int}` |
| `UpdateShippingMethodCommand` | command | `UpdateShippingMethodHandler` | `UpdateShippingMethodValidator` | — | `PUT /api/admin/shipping-methods/{id:int}` |
| `ListShippingMethodsQuery` | query | `ListShippingMethodsHandler` | — | — | `GET /api/admin/shipping-methods` |

| Public contract | Implemented by |
|---|---|
| `IShippingRateProvider` | `StoreShippingRates` |

## Reviews

Module document: [Reviews/README.md](Reviews/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `ApproveReviewCommand` | command | `ApproveReviewHandler` | — | yes | `POST /api/admin/reviews/{id:int}/approve` |
| `CreateReviewCommand` | command | `CreateReviewHandler` | `CreateReviewValidator` | — | `POST /api/products/{productId:int}/reviews` |
| `RejectReviewCommand` | command | `RejectReviewHandler` | `RejectReviewValidator` | yes | `POST /api/admin/reviews/{id:int}/reject` |
| `GetProductReviewsQuery` | query | `GetProductReviewsHandler` | `GetProductReviewsQueryValidator` | — | `GET /api/products/{productId:int}/reviews` |
| `ListReviewsForModerationQuery` | query | `ListReviewsForModerationHandler` | `ListReviewsForModerationValidator` | — | `GET /api/admin/reviews` |

## Notifications

Module document: [Notifications/README.md](Notifications/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `MarkAllNotificationsReadCommand` | command | `MarkAllNotificationsReadHandler` | — | — | `POST /api/notifications/read-all` |
| `MarkNotificationReadCommand` | command | `MarkNotificationReadHandler` | — | — | `POST /api/notifications/{id:int}/read` |
| `CountMyUnreadNotificationsQuery` | query | `CountMyUnreadNotificationsHandler` | — | — | `GET /api/notifications/unread-count` |
| `ListMyNotificationsQuery` | query | `ListMyNotificationsHandler` | `ListMyNotificationsValidator` | — | `GET /api/notifications` |

## Reporting

Module document: [Reporting/README.md](Reporting/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `PurgeBehaviouralEventsCommand` | command | `PurgeBehaviouralEventsHandler` | — | — | no endpoint (sent internally) |
| `RollUpBehaviouralEventsCommand` | command | `RollUpBehaviouralEventsHandler` | — | — | no endpoint (sent internally) |
| `GetPlatformRevenueQuery` | query | `GetPlatformRevenueHandler` | `GetPlatformRevenueQueryValidator` | yes | `GET /api/platform/revenue` |
| `GetPlatformStatsQuery` | query | `GetPlatformStatsHandler` | — | yes | `GET /api/platform/stats` |
| `GetStoreDashboardQuery` | query | `GetStoreDashboardHandler` | `GetStoreDashboardQueryValidator` | yes | `GET /api/admin/reports/dashboard` |

| Public contract | Implemented by |
|---|---|
| `IEventRollups` | `EventRollups` |
| `IEventSink` | `EventBuffer` |
| `IEventStoreRetention` | `EventStoreRetention` |
| `IVisitorContext` | — |

## Billing

Module document: [Billing/README.md](Billing/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `AddMeteredLinesCommand` | command | `AddMeteredLinesHandler` | `AddMeteredLinesValidator` | yes | `POST /api/platform/invoices/{id:int}/metered-lines` |
| `AddPlatformInvoiceLineCommand` | command | `AddPlatformInvoiceLineHandler` | `AddPlatformInvoiceLineValidator` | yes | `POST /api/platform/invoices/{id:int}/lines` |
| `AssignTenantPlanCommand` | command | `AssignTenantPlanHandler` | — | yes | `PUT /api/platform/tenants/{tenantId:int}/plan` |
| `CancelPlatformInvoiceDraftCommand` | command | `CancelPlatformInvoiceDraftHandler` | — | yes | `POST /api/platform/invoices/{id:int}/cancel` |
| `CancelTenantPlanCommand` | command | `CancelTenantPlanHandler` | — | yes | `DELETE /api/platform/tenants/{tenantId:int}/plan` |
| `CloseBillingPeriodCommand` | command | `CloseBillingPeriodHandler` | — | yes | `POST /api/platform/tenants/{tenantId:int}/billing/periods/{periodId:int}/close` |
| `CreatePlanVersionCommand` | command | `CreatePlanVersionHandler` | `CreatePlanVersionValidator` | yes | `POST /api/platform/plans` |
| `CreatePlatformInvoiceCommand` | command | `CreatePlatformInvoiceHandler` | `CreatePlatformInvoiceValidator` | yes | `POST /api/platform/invoices` |
| `GrantEntitlementOverrideCommand` | command | `GrantEntitlementOverrideHandler` | `GrantEntitlementOverrideValidator` | yes | `POST /api/platform/tenants/{tenantId:int}/entitlement-overrides` |
| `IssueCreditNoteCommand` | command | `IssueCreditNoteHandler` | `IssueCreditNoteValidator` | yes | `POST /api/platform/invoices/{id:int}/credit-notes` |
| `IssuePlatformInvoiceCommand` | command | `IssuePlatformInvoiceHandler` | `IssuePlatformInvoiceValidator` | yes | `POST /api/platform/invoices/{id:int}/issue` |
| `PublishPlanCommand` | command | `PublishPlanHandler` | — | yes | `POST /api/platform/plans/{id:int}/publish` |
| `RecordBillableEventCommand` | command | `RecordBillableEventHandler` | `RecordBillableEventValidator` | yes | `POST /api/platform/tenants/{tenantId:int}/billing/events` |
| `RecordInvoicePaymentCommand` | command | `RecordInvoicePaymentHandler` | `RecordInvoicePaymentValidator` | yes | `POST /api/platform/invoices/{id:int}/payments` |
| `RemovePlatformInvoiceLineCommand` | command | `RemovePlatformInvoiceLineHandler` | — | yes | `DELETE /api/platform/invoices/{id:int}/lines/{lineId:int}` |
| `RetirePlanCommand` | command | `RetirePlanHandler` | — | yes | `POST /api/platform/plans/{id:int}/retire` |
| `RevokeEntitlementOverrideCommand` | command | `RevokeEntitlementOverrideHandler` | — | yes | `DELETE /api/platform/tenants/{tenantId:int}/entitlement-overrides/{overrideId:int}` |
| `UpdatePlanDraftCommand` | command | `UpdatePlanDraftHandler` | `UpdatePlanDraftValidator` | yes | `PUT /api/platform/plans/{id:int}` |
| `UpdatePlatformBillingSettingsCommand` | command | `UpdatePlatformBillingSettingsHandler` | `UpdatePlatformBillingSettingsValidator` | yes | `PUT /api/platform/billing/settings` |
| `UpdatePlatformInvoiceNotesCommand` | command | `UpdatePlatformInvoiceNotesHandler` | `UpdatePlatformInvoiceNotesValidator` | yes | `PUT /api/platform/invoices/{id:int}/notes` |
| `GetMyInvoiceQuery` | query | `GetMyInvoiceHandler` | — | yes | `GET /api/admin/store/subscription/invoices/{id:int}` |
| `GetMySubscriptionQuery` | query | `GetMySubscriptionHandler` | — | yes | `GET /api/admin/store/subscription` |
| `GetPlanQuery` | query | `GetPlanHandler` | — | yes | `GET /api/platform/plans/{id:int}` |
| `GetPlatformBillingSettingsQuery` | query | `GetPlatformBillingSettingsHandler` | — | yes | `GET /api/platform/billing/settings` |
| `GetPlatformInvoiceQuery` | query | `GetPlatformInvoiceHandler` | — | yes | `GET /api/platform/invoices/{id:int}` |
| `GetTenantEntitlementsQuery` | query | `GetTenantEntitlementsHandler` | — | yes | `GET /api/platform/tenants/{tenantId:int}/entitlements` |
| `ListBillableEventsQuery` | query | `ListBillableEventsHandler` | — | yes | `GET /api/platform/tenants/{tenantId:int}/billing/periods/{periodId:int}/events` |
| `ListBillingPeriodsQuery` | query | `ListBillingPeriodsHandler` | — | yes | `GET /api/platform/tenants/{tenantId:int}/billing/periods` |
| `ListEntitlementOverridesQuery` | query | `ListEntitlementOverridesHandler` | — | yes | `GET /api/platform/tenants/{tenantId:int}/entitlement-overrides` |
| `ListMyInvoicesQuery` | query | `ListMyInvoicesHandler` | `ListMyInvoicesValidator` | yes | `GET /api/admin/store/subscription/invoices` |
| `ListPlansQuery` | query | `ListPlansHandler` | `ListPlansQueryValidator` | yes | `GET /api/platform/plans` |
| `ListPlatformInvoicesQuery` | query | `ListPlatformInvoicesHandler` | `ListPlatformInvoicesValidator` | yes | `GET /api/platform/invoices` |

| Public contract | Implemented by |
|---|---|
| `IStoreEntitlements` | `StoreEntitlements` |
| `ITenantQuotaGuard` | `TenantQuotaGuard` |

## Tax

Module document: [Tax/README.md](Tax/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `AddTaxProfileVersionCommand` | command | `AddTaxProfileVersionHandler` | `AddTaxProfileVersionValidator` | yes | `POST /api/platform/tax/profiles/{id:int}/versions` |
| `CreateTaxProfileCommand` | command | `CreateTaxProfileHandler` | `CreateTaxProfileValidator` | yes | `POST /api/platform/tax/profiles` |
| `PublishTaxProfileVersionCommand` | command | `PublishTaxProfileVersionHandler` | — | yes | `POST /api/platform/tax/profiles/{id:int}/versions/{versionId:int}/publish` |
| `RequireTaxConfirmationCommand` | command | `RequireTaxConfirmationHandler` | `RequireTaxConfirmationValidator` | yes | `POST /api/platform/tax/profiles/{id:int}/versions/{versionId:int}/require-confirmation` |
| `UpdateStoreTaxSettingsCommand` | command | `UpdateStoreTaxSettingsHandler` | `UpdateStoreTaxSettingsValidator` | yes | `PUT /api/admin/store/tax` |
| `VerifyTaxProfileVersionCommand` | command | `VerifyTaxProfileVersionHandler` | `VerifyTaxProfileVersionValidator` | yes | `POST /api/platform/tax/profiles/{id:int}/versions/{versionId:int}/verify` |
| `GetStoreTaxSettingsQuery` | query | `GetStoreTaxSettingsHandler` | — | yes | `GET /api/admin/store/tax` |
| `GetTaxProfileQuery` | query | `GetTaxProfileHandler` | — | yes | `GET /api/platform/tax/profiles/{id:int}` |
| `ListTaxProfilesQuery` | query | `ListTaxProfilesHandler` | — | yes | `GET /api/admin/store/tax/profiles`<br>`GET /api/platform/tax/profiles` |

| Public contract | Implemented by |
|---|---|
| `ITaxCalculator` | `TaxCalculator` |
