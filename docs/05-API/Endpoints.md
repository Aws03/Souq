# API endpoint inventory

> **Generated from the compiled API** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). Do not edit by hand. After changing a controller, regenerate it with:
> `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
>
> Conventions (errors, paging, status codes): [ApiDocumentation.md](ApiDocumentation.md). Use cases per module: [UseCases.md](../04-MODULES/UseCases.md).

**128 endpoints** in 25 controllers: 24 anonymous, 27 for any signed-in account, 77 behind a permission.

## How to read this table

- **Access:** `anonymous` = `[AllowAnonymous]`; *signed in* = `[Authorize]`, any account; a permission such as `orders.manage` = `[HasPermission]`, granted through roles in `RolePermissions` (401 without a session, 403 without the permission). Several permissions are all required.
- **Hosts:** *store* = served on a store's host only; *platform* = `[PlatformEndpoint]`, platform host only; *store + platform* = `[AvailableOnAllHosts]`. Endpoints on the wrong host answer 404. A store that is not active answers 503 `StoreUnavailable`, except endpoints marked `[AvailableWhenStoreClosed]`, and (during provisioning) `[AvailableDuringProvisioning]` or permission-protected ones. Enforced by `TenantAvailabilityMiddleware`.
- **Module:** `[RequiresModule]`; when the store has the module off, the endpoint answers 404 `ModuleDisabled`.
- **Rate limit:** the `[EnableRateLimiting]` policy (`RateLimitPolicies`).
- **Use case:** the MediatR command or query the action sends, found in the action's IL; its module follows from its namespace.

| Method | Route | Access | Hosts | Module | Rate limit | Use case | Owning module |
|---|---|---|---|---|---|---|---|
| GET | `/api/account/addresses` | signed in | store | — | — | `ListMyAddressesQuery` | Customers |
| POST | `/api/account/addresses` | signed in | store | — | — | `AddMyAddressCommand` | Customers |
| PUT | `/api/account/addresses/{id:int}` | signed in | store | — | — | `UpdateMyAddressCommand` | Customers |
| DELETE | `/api/account/addresses/{id:int}` | signed in | store | — | — | `RemoveMyAddressCommand` | Customers |
| PUT | `/api/account/addresses/{id:int}/default-billing` | signed in | store | — | — | `SetMyDefaultAddressCommand` | Customers |
| PUT | `/api/account/addresses/{id:int}/default-shipping` | signed in | store | — | — | `SetMyDefaultAddressCommand` | Customers |
| POST | `/api/account/erase` | signed in | store | — | — | `EraseMyAccountCommand` | Customers |
| GET | `/api/account/export` | signed in | store | — | — | `ExportMyDataQuery` | Customers |
| GET | `/api/account/profile` | signed in | store | — | — | `GetMyProfileQuery` | Customers |
| PUT | `/api/account/profile` | signed in | store | — | — | `UpdateMyProfileCommand` | Customers |
| GET | `/api/admin/categories` | `catalog.manage` | store | — | — | `ListAdminCategoriesQuery` | Catalog |
| GET | `/api/admin/customers` | `customers.view` | store | — | — | `ListCustomersQuery` | Customers |
| GET | `/api/admin/customers/{id:int}` | `customers.view` | store | — | — | `GetCustomerQuery` | Customers |
| POST | `/api/admin/customers/{id:int}/erase` | `customers.view` + `customers.manage` | store | — | — | `EraseCustomerCommand` | Customers |
| GET | `/api/admin/customers/{id:int}/export` | `customers.view` + `customers.manage` | store | — | — | `ExportCustomerDataQuery` | Customers |
| PUT | `/api/admin/customers/{id:int}/status` | `customers.view` + `customers.manage` | store | — | — | `SetCustomerStatusCommand` | Customers |
| GET | `/api/admin/inventory` | `inventory.view` | store | — | — | `GetInventoryQuery` | Inventory |
| GET | `/api/admin/inventory/low-stock` | `inventory.view` | store | — | — | `GetLowStockQuery` | Inventory |
| POST | `/api/admin/inventory/{productId:int}/adjustments` | `inventory.view` + `inventory.manage` | store | — | — | `AdjustStockCommand` | Inventory |
| GET | `/api/admin/inventory/{productId:int}/movements` | `inventory.view` | store | — | — | `GetStockMovementsQuery` | Inventory |
| PUT | `/api/admin/inventory/{productId:int}/threshold` | `inventory.view` + `inventory.manage` | store | — | — | `SetLowStockThresholdCommand` | Inventory |
| GET | `/api/admin/products` | `catalog.manage` | store | — | — | `ListAdminProductsQuery` | Catalog |
| GET | `/api/admin/products/{id:int}` | `catalog.manage` | store | — | — | `GetAdminProductQuery` | Catalog |
| PUT | `/api/admin/products/{id:int}/images/order` | `catalog.manage` | store | — | — | `ReorderProductImagesCommand` | Catalog |
| DELETE | `/api/admin/products/{id:int}/images/{imageId:int}` | `catalog.manage` | store | — | — | `RemoveProductImageCommand` | Catalog |
| PUT | `/api/admin/products/{id:int}/status` | `catalog.manage` | store | — | — | `ChangeProductStatusCommand` | Catalog |
| GET | `/api/admin/reports/dashboard` | `store.reports.view` | store | — | — | `GetStoreDashboardQuery` | Reporting |
| GET | `/api/admin/reviews` | `reviews.moderate` | store | `reviews` | — | `ListReviewsForModerationQuery` | Reviews |
| GET | `/api/admin/reviews/settings` | `reviews.moderate` | store | `reviews` | — | `GetReviewSettingsQuery` | Platform |
| PUT | `/api/admin/reviews/settings` | `reviews.moderate` + `store.settings.manage` | store | `reviews` | — | `UpdateReviewSettingsCommand` | Platform |
| POST | `/api/admin/reviews/{id:int}/approve` | `reviews.moderate` | store | `reviews` | — | `ApproveReviewCommand` | Reviews |
| POST | `/api/admin/reviews/{id:int}/reject` | `reviews.moderate` | store | `reviews` | — | `RejectReviewCommand` | Reviews |
| GET | `/api/admin/shipping-methods` | `store.shipping.manage` | store | — | — | `ListShippingMethodsQuery` | Shipping |
| POST | `/api/admin/shipping-methods` | `store.shipping.manage` | store | — | — | `CreateShippingMethodCommand` | Shipping |
| PUT | `/api/admin/shipping-methods/{id:int}` | `store.shipping.manage` | store | — | — | `UpdateShippingMethodCommand` | Shipping |
| DELETE | `/api/admin/shipping-methods/{id:int}` | `store.shipping.manage` | store | — | — | `DeleteShippingMethodCommand` | Shipping |
| GET | `/api/admin/staff` | `store.staff.manage` | store | — | — | `ListStaffQuery` | Identity |
| POST | `/api/admin/staff` | `store.staff.manage` | store | — | — | `InviteStaffCommand` | Identity |
| POST | `/api/admin/staff/{id:int}/status` | `store.staff.manage` | store | — | — | `SetStaffStatusCommand` | Identity |
| POST | `/api/admin/store/branding/favicon` | `store.settings.manage` | store | — | — | — | — |
| POST | `/api/admin/store/branding/logo` | `store.settings.manage` | store | — | — | — | — |
| POST | `/api/admin/store/branding/social-image` | `store.settings.manage` | store | — | — | — | — |
| GET | `/api/admin/store/payments` | `store.payments.manage` | store | — | — | `GetStorePaymentAccountQuery` | Platform |
| PUT | `/api/admin/store/payments` | `store.payments.manage` | store | — | — | `UpdateStorePaymentAccountCommand` | Platform |
| DELETE | `/api/admin/store/payments` | `store.payments.manage` | store | — | — | `RemoveStorePaymentAccountCommand` | Platform |
| GET | `/api/admin/store/settings` | `store.settings.manage` | store | — | — | `GetStoreSettingsQuery` | Platform |
| PUT | `/api/admin/store/settings` | `store.settings.manage` | store | — | — | `UpdateStoreSettingsCommand` | Platform |
| GET | `/api/admin/store/settings/options` | `store.settings.manage` | store | — | — | `GetStoreSettingsOptionsQuery` | Platform |
| POST | `/api/auth/change-password` | signed in | store + platform, during provisioning | — | `auth` | `ChangePasswordCommand` | Identity |
| POST | `/api/auth/forgot-password` | anonymous | store + platform, during provisioning | — | `auth` | `ForgotPasswordCommand` | Identity |
| POST | `/api/auth/login` | anonymous | store + platform, even when closed | — | `auth` | `LoginCommand` | Identity |
| POST | `/api/auth/logout` | anonymous | store + platform, even when closed | — | — | `LogoutCommand` | Identity |
| GET | `/api/auth/me` | signed in | store + platform, even when closed | — | — | `GetCurrentUserQuery` | Identity |
| POST | `/api/auth/refresh` | anonymous | store + platform, even when closed | — | `auth-refresh` | `RefreshSessionCommand` | Identity |
| POST | `/api/auth/register` | anonymous | store + platform, during provisioning | — | `auth` | `RegisterCommand` | Identity |
| POST | `/api/auth/resend-verification` | signed in | store + platform, during provisioning | — | `auth` | `ResendVerificationCommand` | Identity |
| POST | `/api/auth/reset-password` | anonymous | store + platform, during provisioning | — | `auth` | `ResetPasswordCommand` | Identity |
| POST | `/api/auth/verify-email` | anonymous | store + platform, during provisioning | — | `auth` | `VerifyEmailCommand` | Identity |
| GET | `/api/basket` | anonymous | store | — | — | `GetBasketQuery` | Shopping |
| DELETE | `/api/basket` | anonymous | store | — | `basket` | `ClearBasketCommand` | Shopping |
| POST | `/api/basket/items` | anonymous | store | — | `basket` | `AddBasketItemCommand` | Shopping |
| PUT | `/api/basket/items/{productId:int}` | anonymous | store | — | `basket` | `SetBasketItemQuantityCommand` | Shopping |
| DELETE | `/api/basket/items/{productId:int}` | anonymous | store | — | `basket` | `RemoveBasketItemCommand` | Shopping |
| GET | `/api/basket/quote` | anonymous | store | — | `coupon-preview` | `GetBasketQuery` | Shopping |
| GET | `/api/categories` | anonymous | store | — | — | `GetCategoriesQuery` | Catalog |
| POST | `/api/categories` | `catalog.manage` | store | — | — | `CreateCategoryCommand` | Catalog |
| PUT | `/api/categories/{id:int}` | `catalog.manage` | store | — | — | `UpdateCategoryCommand` | Catalog |
| DELETE | `/api/categories/{id:int}` | `catalog.manage` | store | — | — | `DeleteCategoryCommand` | Catalog |
| GET | `/api/coupons` | `promotions.manage` | store | `promotions` | — | `GetCouponsQuery` | Promotions |
| POST | `/api/coupons` | `promotions.manage` | store | `promotions` | — | `CreateCouponCommand` | Promotions |
| GET | `/api/coupons/apply` | anonymous | store | `promotions` | `coupon-preview` | `ApplyCouponQuery` | Promotions |
| PUT | `/api/coupons/{id:int}` | `promotions.manage` | store | `promotions` | — | `UpdateCouponCommand` | Promotions |
| DELETE | `/api/coupons/{id:int}` | `promotions.manage` | store | `promotions` | — | `DeleteCouponCommand` | Promotions |
| GET | `/api/coupons/{id:int}/redemptions` | `promotions.manage` | store | `promotions` | — | `GetCouponRedemptionsQuery` | Promotions |
| GET | `/api/notifications` | signed in | store | — | — | `ListMyNotificationsQuery` | Notifications |
| POST | `/api/notifications/read-all` | signed in | store | — | — | `MarkAllNotificationsReadCommand` | Notifications |
| GET | `/api/notifications/unread-count` | signed in | store | — | — | `CountMyUnreadNotificationsQuery` | Notifications |
| POST | `/api/notifications/{id:int}/read` | signed in | store | — | — | `MarkNotificationReadCommand` | Notifications |
| GET | `/api/orders` | `orders.view` | store | — | — | `GetOrdersQuery` | Ordering |
| POST | `/api/orders` | signed in | store | — | — | `CreateOrderCommand` | Ordering |
| GET | `/api/orders/mine` | signed in | store | — | — | `GetMyOrdersQuery` | Ordering |
| GET | `/api/orders/track/{token}` | anonymous | store | — | — | `GetOrderTrackingQuery` | Ordering |
| GET | `/api/orders/{id:int}` | signed in | store | — | — | `GetOrderByIdQuery` | Ordering |
| POST | `/api/orders/{id:int}/cancel` | signed in | store | — | — | `CancelMyOrderCommand` | Ordering |
| POST | `/api/orders/{id:int}/confirm-payment` | signed in | store | — | — | `ConfirmOrderPaymentCommand` | Ordering |
| POST | `/api/orders/{id:int}/refunds` | `store.payments.manage` | store | — | — | `RefundOrderCommand` | Payments |
| POST | `/api/orders/{id:int}/refunds/{refundId:int}/retry` | `store.payments.manage` | store | — | — | `RetryRefundCommand` | Payments |
| PUT | `/api/orders/{id:int}/status` | `orders.manage` | store | — | — | `UpdateOrderStatusCommand` | Ordering |
| GET | `/api/payments/config` | anonymous | store | — | — | `GetPaymentConfigQuery` | Payments |
| POST | `/api/payments/webhook` | anonymous | store | — | — | `ProcessPaymentWebhookCommand` | Ordering |
| GET | `/api/platform/audit` | `platform.audit.view` | platform | — | — | `ListAuditEntriesQuery` | Platform |
| GET | `/api/platform/stats` | `platform.reports.view` | platform | — | — | `GetPlatformStatsQuery` | Reporting |
| GET | `/api/platform/tenants` | `platform.tenants.manage` | platform | — | — | `ListTenantsQuery` | Platform |
| POST | `/api/platform/tenants` | `platform.tenants.manage` | platform | — | — | `CreateTenantCommand` | Platform |
| GET | `/api/platform/tenants/{id:int}` | `platform.tenants.manage` | platform | — | — | `GetTenantQuery` | Platform |
| PUT | `/api/platform/tenants/{id:int}` | `platform.tenants.manage` | platform | — | — | `UpdateTenantCommand` | Platform |
| GET | `/api/platform/tenants/{id:int}/accounts` | `platform.tenants.manage` | platform | — | — | `ListTenantAccountsQuery` | Platform |
| POST | `/api/platform/tenants/{id:int}/admins` | `platform.tenants.manage` | platform | — | — | `InviteTenantAdminCommand` | Platform |
| POST | `/api/platform/tenants/{id:int}/branding/{asset}` | `platform.tenants.manage` | platform | — | — | `UploadTenantBrandingCommand` | Platform |
| POST | `/api/platform/tenants/{id:int}/domains` | `platform.tenants.manage` | platform | — | — | `ChangeTenantDomainCommand` | Platform |
| DELETE | `/api/platform/tenants/{id:int}/domains/{host}` | `platform.tenants.manage` | platform | — | — | `ChangeTenantDomainCommand` | Platform |
| POST | `/api/platform/tenants/{id:int}/domains/{host}/primary` | `platform.tenants.manage` | platform | — | — | `ChangeTenantDomainCommand` | Platform |
| POST | `/api/platform/tenants/{id:int}/domains/{host}/verify` | `platform.tenants.manage` | platform | — | — | `ChangeTenantDomainCommand` | Platform |
| PUT | `/api/platform/tenants/{id:int}/modules` | `platform.tenants.manage` | platform | — | — | `SetTenantModulesCommand` | Platform |
| GET | `/api/platform/tenants/{id:int}/payments` | `platform.tenants.manage` | platform | — | — | `GetTenantPaymentAccountQuery` | Platform |
| PUT | `/api/platform/tenants/{id:int}/payments` | `platform.tenants.manage` | platform | — | — | `UpdateTenantPaymentAccountCommand` | Platform |
| DELETE | `/api/platform/tenants/{id:int}/payments` | `platform.tenants.manage` | platform | — | — | `RemoveTenantPaymentAccountCommand` | Platform |
| PUT | `/api/platform/tenants/{id:int}/settings` | `platform.tenants.manage` | platform | — | — | `UpdateTenantSettingsCommand` | Platform |
| POST | `/api/platform/tenants/{id:int}/status` | `platform.tenants.manage` | platform | — | — | `ChangeTenantStatusCommand` | Platform |
| GET | `/api/platform/users` | `platform.users.manage` | platform | — | — | `ListPlatformUsersQuery` | Platform |
| POST | `/api/platform/users` | `platform.users.manage` | platform | — | — | `InvitePlatformUserCommand` | Platform |
| POST | `/api/platform/users/{id:int}/status` | `platform.users.manage` | platform | — | — | `SetPlatformUserStatusCommand` | Platform |
| GET | `/api/products` | anonymous | store | — | — | `GetProductsQuery` | Catalog |
| POST | `/api/products` | `catalog.manage` | store | — | — | `CreateProductCommand` | Catalog |
| GET | `/api/products/by-slug/{slug}` | anonymous | store | — | — | `GetProductBySlugQuery` | Catalog |
| GET | `/api/products/{id:int}` | anonymous | store | — | — | `GetProductByIdQuery` | Catalog |
| PUT | `/api/products/{id:int}` | `catalog.manage` | store | — | — | `UpdateProductCommand` | Catalog |
| DELETE | `/api/products/{id:int}` | `catalog.manage` | store | — | — | `DeleteProductCommand` | Catalog |
| POST | `/api/products/{id:int}/image` | `catalog.manage` | store | — | — | `UploadProductImageCommand` | Catalog |
| GET | `/api/products/{id:int}/related` | anonymous | store | — | — | `GetRelatedProductsQuery` | Catalog |
| POST | `/api/products/{id:int}/video` | `catalog.manage` | store | — | — | `UploadProductVideoCommand` | Catalog |
| GET | `/api/products/{productId:int}/reviews` | anonymous | store | `reviews` | — | `GetProductReviewsQuery` | Reviews |
| POST | `/api/products/{productId:int}/reviews` | signed in | store | `reviews` | — | `CreateReviewCommand` | Reviews |
| GET | `/api/storefront/config` | anonymous | store, even when closed | — | — | `GetStorefrontConfigQuery` | Platform |
| GET | `/api/wishlist` | signed in | store | `wishlist` | — | `GetWishlistQuery` | Shopping |
| POST | `/api/wishlist/merge` | signed in | store | `wishlist` | — | `MergeWishlistCommand` | Shopping |
| PUT | `/api/wishlist/{productId:int}` | signed in | store | `wishlist` | — | `AddToWishlistCommand` | Shopping |
| DELETE | `/api/wishlist/{productId:int}` | signed in | store | `wishlist` | — | `RemoveFromWishlistCommand` | Shopping |
