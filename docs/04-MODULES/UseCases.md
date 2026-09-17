# Use-case catalog

> **Generated from the compiled code** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). Do not edit by hand; regenerate with:
> `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
>
> Every MediatR command and query per module, with its handler, its validator, whether it is audited, and the endpoints that send it; then each module's public contracts (the interfaces other modules may call) and what implements them. Module docs explain the *why*: [Modules.md](Modules.md). Endpoints: [Endpoints.md](../05-API/Endpoints.md).

| Module | Feature folders | Commands | Queries | Contracts |
|---|---|---|---|---|
| [Platform](#platform) | `src/Souq.Application/Features/Platform`, `src/Souq.Application/Features/Stores` | 17 | 12 | 0 |
| [Identity](#identity) | `src/Souq.Application/Features/Auth`, `src/Souq.Application/Features/Staff` | 11 | 2 | 0 |
| [Catalog](#catalog) | `src/Souq.Application/Features/Products`, `src/Souq.Application/Features/Categories` | 16 | 8 | 1 |
| [Inventory](#inventory) | `src/Souq.Application/Features/Inventory` | 4 | 4 | 2 |
| [Customers](#customers) | `src/Souq.Application/Features/Customers` | 8 | 6 | 0 |
| [Shopping](#shopping) | `src/Souq.Application/Features/Baskets`, `src/Souq.Application/Features/Wishlist` | 10 | 2 | 2 |
| [Ordering](#ordering) | `src/Souq.Application/Features/Orders` | 7 | 4 | 0 |
| [Payments](#payments) | `src/Souq.Application/Features/Payments` | 2 | 1 | 2 |
| [Promotions](#promotions) | `src/Souq.Application/Features/Coupons` | 3 | 3 | 1 |
| [Shipping](#shipping) | `src/Souq.Application/Features/Shipping` | 3 | 1 | 1 |
| [Reviews](#reviews) | `src/Souq.Application/Features/Reviews` | 3 | 2 | 0 |
| [Notifications](#notifications) | `src/Souq.Application/Features/Notifications` | 2 | 2 | 0 |
| [Reporting](#reporting) | `src/Souq.Application/Features/Reporting` | 0 | 2 | 0 |

## Platform

Module document: [Platform/README.md](Platform/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `ChangeTenantDomainCommand` | command | `ChangeTenantDomainHandler` | `ChangeTenantDomainValidator` | yes | `POST /api/platform/tenants/{id:int}/domains`<br>`DELETE /api/platform/tenants/{id:int}/domains/{host}`<br>`POST /api/platform/tenants/{id:int}/domains/{host}/primary`<br>`POST /api/platform/tenants/{id:int}/domains/{host}/verify` |
| `ChangeTenantStatusCommand` | command | `ChangeTenantStatusHandler` | `ChangeTenantStatusValidator` | yes | `POST /api/platform/tenants/{id:int}/status` |
| `CreateTenantCommand` | command | `CreateTenantHandler` | `CreateTenantValidator` | yes | `POST /api/platform/tenants` |
| `InvitePlatformUserCommand` | command | `InvitePlatformUserHandler` | `InvitePlatformUserValidator` | yes | `POST /api/platform/users` |
| `InviteTenantAdminCommand` | command | `InviteTenantAdminHandler` | `InviteTenantAdminValidator` | yes | `POST /api/platform/tenants/{id:int}/admins` |
| `RemoveStorePaymentAccountCommand` | command | `RemoveStorePaymentAccountHandler` | — | yes | `DELETE /api/admin/store/payments` |
| `RemoveTenantPaymentAccountCommand` | command | `RemoveTenantPaymentAccountHandler` | — | yes | `DELETE /api/platform/tenants/{id:int}/payments` |
| `SetPlatformUserStatusCommand` | command | `SetPlatformUserStatusHandler` | — | yes | `POST /api/platform/users/{id:int}/status` |
| `SetTenantModulesCommand` | command | `SetTenantModulesHandler` | `SetTenantModulesValidator` | yes | `PUT /api/platform/tenants/{id:int}/modules` |
| `UpdateReviewSettingsCommand` | command | `UpdateReviewSettingsHandler` | — | yes | `PUT /api/admin/reviews/settings` |
| `UpdateStorePaymentAccountCommand` | command | `UpdateStorePaymentAccountHandler` | `UpdateStorePaymentAccountValidator` | yes | `PUT /api/admin/store/payments` |
| `UpdateStoreSettingsCommand` | command | `UpdateStoreSettingsHandler` | `UpdateStoreSettingsValidator` | yes | `PUT /api/admin/store/settings` |
| `UpdateTenantCommand` | command | `UpdateTenantHandler` | `UpdateTenantValidator` | yes | `PUT /api/platform/tenants/{id:int}` |
| `UpdateTenantPaymentAccountCommand` | command | `UpdateTenantPaymentAccountHandler` | `UpdateTenantPaymentAccountValidator` | yes | `PUT /api/platform/tenants/{id:int}/payments` |
| `UpdateTenantSettingsCommand` | command | `UpdateTenantSettingsHandler` | `UpdateTenantSettingsValidator` | yes | `PUT /api/platform/tenants/{id:int}/settings` |
| `UploadStoreBrandingCommand` | command | `UploadStoreBrandingHandler` | — | yes | no endpoint (sent internally) |
| `UploadTenantBrandingCommand` | command | `UploadTenantBrandingHandler` | — | yes | `POST /api/platform/tenants/{id:int}/branding/{asset}` |
| `GetProvisioningOptionsQuery` | query | `GetProvisioningOptionsHandler` | — | yes | `GET /api/platform/tenants/options` |
| `GetReviewSettingsQuery` | query | `GetReviewSettingsHandler` | — | — | `GET /api/admin/reviews/settings` |
| `GetStorePaymentAccountQuery` | query | `GetStorePaymentAccountHandler` | — | — | `GET /api/admin/store/payments` |
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

## Catalog

Module document: [Catalog/README.md](Catalog/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `ChangeProductStatusCommand` | command | `ChangeProductStatusHandler` | `ChangeProductStatusValidator` | yes | `PUT /api/admin/products/{id:int}/status` |
| `CreateCategoryCommand` | command | `CreateCategoryHandler` | `CreateCategoryValidator` | yes | `POST /api/categories` |
| `CreateProductCommand` | command | `CreateProductHandler` | `CreateProductValidator` | yes | `POST /api/products` |
| `CreateProductVariantsCommand` | command | `CreateProductVariantsHandler` | `CreateProductVariantsValidator` | yes | `POST /api/admin/products/{id:int}/variants` |
| `DeleteCategoryCommand` | command | `DeleteCategoryHandler` | `DeleteCategoryValidator` | yes | `DELETE /api/categories/{id:int}` |
| `DeleteProductCommand` | command | `DeleteProductHandler` | `DeleteProductValidator` | yes | `DELETE /api/products/{id:int}` |
| `RemoveProductImageCommand` | command | `RemoveProductImageHandler` | — | yes | `DELETE /api/admin/products/{id:int}/images/{imageId:int}` |
| `ReorderProductImagesCommand` | command | `ReorderProductImagesHandler` | `ReorderProductImagesValidator` | yes | `PUT /api/admin/products/{id:int}/images/order` |
| `SetDefaultProductVariantCommand` | command | `SetDefaultProductVariantHandler` | `SetDefaultProductVariantValidator` | yes | `PUT /api/admin/products/{id:int}/variants/{variantId:int}/default` |
| `SetProductOptionsCommand` | command | `SetProductOptionsHandler` | `SetProductOptionsValidator` | yes | `PUT /api/admin/products/{id:int}/options` |
| `SetProductVariantStatusCommand` | command | `SetProductVariantStatusHandler` | `SetProductVariantStatusValidator` | yes | `PUT /api/admin/products/{id:int}/variants/{variantId:int}/status` |
| `UpdateCategoryCommand` | command | `UpdateCategoryHandler` | `UpdateCategoryValidator` | yes | `PUT /api/categories/{id:int}` |
| `UpdateProductCommand` | command | `UpdateProductHandler` | `UpdateProductValidator` | yes | `PUT /api/products/{id:int}` |
| `UpdateProductVariantCommand` | command | `UpdateProductVariantHandler` | `UpdateProductVariantValidator` | yes | `PUT /api/admin/products/{id:int}/variants/{variantId:int}` |
| `UploadProductImageCommand` | command | `UploadProductImageHandler` | — | yes | `POST /api/products/{id:int}/image` |
| `UploadProductVideoCommand` | command | `UploadProductVideoHandler` | — | yes | `POST /api/products/{id:int}/video` |
| `GetAdminProductQuery` | query | `GetAdminProductHandler` | — | — | `GET /api/admin/products/{id:int}` |
| `GetCategoriesQuery` | query | `GetCategoriesHandler` | — | — | `GET /api/categories` |
| `GetProductByIdQuery` | query | `GetProductByIdHandler` | — | — | `GET /api/products/{id:int}` |
| `GetProductBySlugQuery` | query | `GetProductByIdHandler` | `GetProductBySlugQueryValidator` | — | `GET /api/products/by-slug/{slug}` |
| `GetProductsQuery` | query | `GetProductsHandler` | `GetProductsQueryValidator` | — | `GET /api/products` |
| `GetRelatedProductsQuery` | query | `GetRelatedProductsHandler` | `GetRelatedProductsQueryValidator` | — | `GET /api/products/{id:int}/related` |
| `ListAdminCategoriesQuery` | query | `GetCategoriesHandler` | — | — | `GET /api/admin/categories` |
| `ListAdminProductsQuery` | query | `ListAdminProductsHandler` | `ListAdminProductsQueryValidator` | — | `GET /api/admin/products` |

| Public contract | Implemented by |
|---|---|
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
| `SetMyDefaultAddressCommand` | command | `SetMyDefaultAddressHandler` | — | — | `PUT /api/account/addresses/{id:int}/default-billing`<br>`PUT /api/account/addresses/{id:int}/default-shipping` |
| `UpdateMyAddressCommand` | command | `UpdateMyAddressHandler` | `UpdateMyAddressValidator` | — | `PUT /api/account/addresses/{id:int}` |
| `UpdateMyProfileCommand` | command | `UpdateMyProfileHandler` | `UpdateMyProfileValidator` | — | `PUT /api/account/profile` |
| `ExportCustomerDataQuery` | query | `ExportCustomerDataHandler` | — | yes | `GET /api/admin/customers/{id:int}/export` |
| `ExportMyDataQuery` | query | `ExportMyDataHandler` | — | yes | `GET /api/account/export` |
| `GetCustomerQuery` | query | `GetCustomerHandler` | — | — | `GET /api/admin/customers/{id:int}` |
| `GetMyProfileQuery` | query | `GetMyProfileHandler` | — | — | `GET /api/account/profile` |
| `ListCustomersQuery` | query | `ListCustomersHandler` | `ListCustomersQueryValidator` | — | `GET /api/admin/customers` |
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
| `RetryRefundCommand` | command | `RetryRefundHandler` | — | yes | `POST /api/orders/{id:int}/refunds/{refundId:int}/retry` |
| `GetPaymentConfigQuery` | query | `GetPaymentConfigHandler` | — | — | `GET /api/payments/config` |

| Public contract | Implemented by |
|---|---|
| `IOrderPayments` | `OrderPayments` |
| `IPaymentQueries` | `PaymentQueries` |

## Promotions

Module document: [Promotions/README.md](Promotions/README.md).

| Use case | Kind | Handler | Validator | Audited | Sent by |
|---|---|---|---|---|---|
| `CreateCouponCommand` | command | `CreateCouponHandler` | `CreateCouponValidator` | — | `POST /api/coupons` |
| `DeleteCouponCommand` | command | `DeleteCouponHandler` | `DeleteCouponValidator` | — | `DELETE /api/coupons/{id:int}` |
| `UpdateCouponCommand` | command | `UpdateCouponHandler` | `UpdateCouponValidator` | — | `PUT /api/coupons/{id:int}` |
| `ApplyCouponQuery` | query | `ApplyCouponHandler` | — | — | `GET /api/coupons/apply` |
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
| `GetPlatformStatsQuery` | query | `GetPlatformStatsHandler` | — | yes | `GET /api/platform/stats` |
| `GetStoreDashboardQuery` | query | `GetStoreDashboardHandler` | — | yes | `GET /api/admin/reports/dashboard` |
