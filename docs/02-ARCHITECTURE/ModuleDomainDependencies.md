# Cross-module domain dependencies

> **Generated from the compiled Application assembly** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). Do not edit by hand; regenerate with:
> `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`

## What this file is, and why it fails your build

Modules are supposed to reach each other only through contracts ([ModuleBoundaries.md](ModuleBoundaries.md)). `ModuleAndContractRuleTests` enforces that **only inside `Souq.Application.Features`**: repository ports live in `Souq.Domain.Interfaces` and most entities in `Souq.Domain.Entities`, so a handler that loads another module's aggregate directly breaks no test.

This file makes those crossings countable. It lists every place one module's use cases touch a Domain type another module owns (ownership: the `DomainOwners` map in `tests/Souq.ArchitectureTests/ModuleMap.cs`; shared-kernel types such as `Money` and `Entity` are not crossings). The committed file is a ratchet:

- **A new crossing changes this file and fails the test.** That is the point: it should be a decision, made in review, not a quiet import. Prefer adding a contract to the owning module; if the crossing is deliberate, regenerate the file so the diff shows what you added.
- **Removing a crossing also changes this file.** Regenerate, and the count goes down.

**Today: 65 crossings across 13 module pairs.**

| From | To | Crossings |
|---|---|---|
| Notifications | Identity | 14 |
| Shopping | Catalog | 12 |
| Notifications | Ordering | 9 |
| Notifications | Platform | 6 |
| Customers | Shopping | 4 |
| Notifications | Customers | 4 |
| Ordering | Customers | 4 |
| Shopping | Promotions | 4 |
| Notifications | Catalog | 2 |
| Reviews | Customers | 2 |
| Reviews | Platform | 2 |
| Notifications | Inventory | 1 |
| Reviews | Ordering | 1 |

## Every crossing

| From | To | Domain type | Used by |
|---|---|---|---|
| Customers | Shopping | `Basket` | `CustomerErasure` |
| Customers | Shopping | `IBasketRepository` | `CustomerErasure` |
| Customers | Shopping | `IWishlistRepository` | `CustomerErasure` |
| Customers | Shopping | `WishlistItem` | `CustomerErasure` |
| Shopping | Catalog | `CatalogTranslation` | `PricingService` |
| Shopping | Catalog | `IProductRepository` | `AddBasketItemHandler` |
| Shopping | Catalog | `IProductRepository` | `AddToWishlistHandler` |
| Shopping | Catalog | `IProductRepository` | `MergeWishlistHandler` |
| Shopping | Catalog | `IProductRepository` | `PricingService` |
| Shopping | Catalog | `Product` | `AddBasketItemHandler` |
| Shopping | Catalog | `Product` | `AddToWishlistHandler` |
| Shopping | Catalog | `Product` | `MergeWishlistHandler` |
| Shopping | Catalog | `Product` | `PricingService` |
| Shopping | Catalog | `ProductTranslation` | `PricingService` |
| Shopping | Catalog | `ProductVariant` | `AddBasketItemHandler` |
| Shopping | Catalog | `ProductVariant` | `PricingService` |
| Shopping | Promotions | `Coupon` | `PricingService` |
| Shopping | Promotions | `ICouponRedemptionRepository` | `PricingService` |
| Shopping | Promotions | `ICouponRepository` | `PricingService` |
| Shopping | Promotions | `InvalidCouponException` | `PricingService` |
| Ordering | Customers | `Customer` | `CheckoutQuote` |
| Ordering | Customers | `CustomerAddress` | `CheckoutQuote` |
| Ordering | Customers | `ICustomerRepository` | `CheckoutQuote` |
| Ordering | Customers | `PostalAddress` | `CheckoutQuote` |
| Reviews | Customers | `Customer` | `CreateReviewHandler` |
| Reviews | Customers | `ICustomerRepository` | `CreateReviewHandler` |
| Reviews | Ordering | `IOrderRepository` | `CreateReviewHandler` |
| Reviews | Platform | `ITenantRepository` | `CreateReviewHandler` |
| Reviews | Platform | `Tenant` | `CreateReviewHandler` |
| Notifications | Catalog | `IProductRepository` | `StockBecameLowHandler` |
| Notifications | Catalog | `Product` | `StockBecameLowHandler` |
| Notifications | Customers | `Customer` | `OrderEmailHandler` |
| Notifications | Customers | `Customer` | `OrderStatusChangedHandler` |
| Notifications | Customers | `ICustomerRepository` | `OrderEmailHandler` |
| Notifications | Customers | `ICustomerRepository` | `OrderStatusChangedHandler` |
| Notifications | Identity | `IUserRepository` | `EmailVerificationEmailHandler` |
| Notifications | Identity | `IUserRepository` | `InvitationEmailHandler` |
| Notifications | Identity | `IUserRepository` | `OrderStatusChangedHandler` |
| Notifications | Identity | `IUserRepository` | `PasswordChangedEmailHandler` |
| Notifications | Identity | `IUserRepository` | `PasswordResetEmailHandler` |
| Notifications | Identity | `IUserRepository` | `StockBecameLowHandler` |
| Notifications | Identity | `User` | `EmailVerificationEmailHandler` |
| Notifications | Identity | `User` | `InvitationEmailHandler` |
| Notifications | Identity | `User` | `PasswordChangedEmailHandler` |
| Notifications | Identity | `User` | `PasswordResetEmailHandler` |
| Notifications | Identity | `UserStatus` | `EmailVerificationEmailHandler` |
| Notifications | Identity | `UserStatus` | `InvitationEmailHandler` |
| Notifications | Identity | `UserStatus` | `PasswordChangedEmailHandler` |
| Notifications | Identity | `UserStatus` | `PasswordResetEmailHandler` |
| Notifications | Inventory | `StockBecameLow` | `StockBecameLowHandler` |
| Notifications | Ordering | `IOrderRepository` | `OrderEmailHandler` |
| Notifications | Ordering | `IOrderRepository` | `OrderStatusChangedHandler` |
| Notifications | Ordering | `Order` | `OrderEmailHandler` |
| Notifications | Ordering | `Order` | `OrderStatusChangedHandler` |
| Notifications | Ordering | `OrderActorKind` | `OrderStatusChangedHandler` |
| Notifications | Ordering | `OrderItem` | `OrderEmailHandler` |
| Notifications | Ordering | `OrderStatus` | `OrderEmailHandler` |
| Notifications | Ordering | `OrderStatus` | `OrderStatusChangedHandler` |
| Notifications | Ordering | `OrderStatusChanged` | `OrderStatusChangedHandler` |
| Notifications | Platform | `BrandColors` | `NotificationEmails` |
| Notifications | Platform | `ITenantRepository` | `NotificationEmails` |
| Notifications | Platform | `StoreBranding` | `NotificationEmails` |
| Notifications | Platform | `StoreContact` | `NotificationEmails` |
| Notifications | Platform | `StoreSettings` | `NotificationEmails` |
| Notifications | Platform | `Tenant` | `NotificationEmails` |
