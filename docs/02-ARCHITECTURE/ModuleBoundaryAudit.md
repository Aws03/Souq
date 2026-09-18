# Module boundary audit

> **What this page is:** every one of the cross-module domain crossings in [ModuleDomainDependencies.md](ModuleDomainDependencies.md), read in the code and given a class and a reason. The generated file *counts* crossings; this page says which ones are fine, which are accidents, and which are architectural violations.
> **Read with:** [ModuleBoundaries.md](ModuleBoundaries.md) (who owns what) · [DependencyRules.md](DependencyRules.md) (what enforces what) · [ADR-0004](../11-ADR/0004-module-boundaries.md).
> **Audited:** Phase 17, against 79 crossings across 16 module pairs. Row 79 was added during the phase itself — the ratchet caught it — and is recorded at the end of the table rather than renumbering the rest. **Re-verified and updated in the M1 architecture audit** ([SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md)): rank 3 below (Platform → Payments, rows 1–5 of the original table) is closed — `IStorePaymentAccountEditor` is now a real published contract in `Souq.Application.Features.Payments.Contracts`, not a raw class reference — leaving **74 crossings across 15 module pairs**. The rest of the audit was re-read against the current code and still holds; the table below is renumbered 1–74 to match the regenerated [ModuleDomainDependencies.md](ModuleDomainDependencies.md) rather than leaving a gap, because the removed rows sat at the very start of the table and a gap there would misread as "row 1 does not exist" rather than "closed."

## The goal is not zero

A crossing is not a defect by itself. This is a modular monolith: one transaction may span modules ([ADR-0021](../11-ADR/0021-transaction-boundaries.md)), and a handler reading another module's aggregate is sometimes the honest shape. The objective is that **every crossing has a reason someone chose**, and that the dangerous ones are known.

## Classes

| Class | Meaning | Count |
|---|---|---|
| **A** | Goes through a published contract | **0** — see below |
| **B** | A legitimate shared primitive: a published domain event, or a store setting the owning module publishes | **13** |
| **C** | Accidental coupling: a read that a narrow contract would serve just as well | **43** |
| **D** | Architectural violation: one module **writes** another module's aggregate, or the crossing forms a cycle | **18** |
| **E** | Unclear, needs an owner decision | **0** |

**Why A is zero, and why that is not alarming.** The generator only counts references to `Souq.Domain` types. Contracts live in `Souq.Application.Features.*.Contracts`, so a crossing that goes *through* a contract is invisible to it by construction. This file therefore measures exactly the crossings that **bypass** contracts. The ten contracts that do exist — `IPricing`, `IBasketCheckout`, `ICouponRedemptions`, `IInventoryReservations`, `IStockAvailability`, `IOrderPayments`, `IPaymentQueries`, `IShippingRateProvider`, `IVariantStockInitializer`, `IStorePaymentAccountEditor` — are working: `CreateOrderHandler` reaches Shopping, Promotions, Payments, Inventory and Shipping entirely through them, its only raw crossing is `ICustomerRepository`; and `Features/Platform/TenantPaymentAccounts.cs` reaches Payments entirely through `IStorePaymentAccountEditor` (closed in the M1 architecture audit — TD-04/R-04).

**Why E is zero.** Every crossing's *mechanism* is unambiguous once read. Two *remedies* need an owner decision, and are marked as such.

## The 18 that matter: one module writing another's aggregate

These are the whole of class D — four call sites, in the handlers the "Rows" column names:

| Call site | Writes | Rows |
|---|---|---|
| `src/Souq.Application/Features/Auth/Commands/Register.cs` | Identity creates the **Customers** aggregate (`new Customer(...)`, `_customers.AddAsync`) | 1, 4 |
| `src/Souq.Application/Features/Customers/CustomerErasure.cs` | Customers erases Identity's `User` and revokes its `RefreshToken`s | 5, 6, 9, 10 |
| `src/Souq.Application/Features/Customers/CustomerErasure.cs` | Customers **deletes** Shopping's `Basket` and every `WishlistItem` | 13–16 |
| `src/Souq.Application/Features/Customers/Account/AccountUseCases.cs` | Customers renames Identity's `User` | 8, 12 |
| `src/Souq.Application/Features/Notifications/IdentityEmailHandlers.cs` | Notifications mints and persists tokens on Identity's `User` | 48, 49, 51, 53, 54, 55 |

**Closed in the M1 architecture audit:** the store-side payment-account file used to appear here, at *Features/Stores/StorePaymentAccounts.cs* — the Platform folder wrote Payments' `StorePaymentAccount` aggregate directly. It moved to `src/Souq.Application/Features/Payments/StorePaymentAccounts.cs` and now implements a published contract (`IStorePaymentAccountEditor`, `src/Souq.Application/Features/Payments/Contracts/StorePaymentAccountContracts.cs`); `src/Souq.Application/Features/Platform/TenantPaymentAccounts.cs` — which must stay in Platform because it carries a `TenantId` from the platform admin API — calls that contract through `ITenantScopeRunner`, not the concrete class. See "A trap in rank 3" below for why the two files could not simply be moved together without one.

Two observations worth carrying:

- **`CustomerErasure` writes three other modules' aggregates in one `SaveChangesAsync`**, and it is reached from two commands (a customer's own erasure and the admin's). It is the single most coupled class in the system — and the coupling is real work, not sloppiness: erasure genuinely must end sessions and delete baskets.
- **Notifications is a writer, not just a reader.** Six of its eleven Identity crossings persist a token on `User`. That is deliberate ([ADR-0034](../11-ADR/0034-notifications-outbox.md): no raw token at rest), but it means the notification dispatcher writes Identity's aggregate, which matters a great deal before extracting Notifications into a service.

## Per-pair summary

| Pair | B | C | D | The one contract that would close it |
|---|---|---|---|---|
| Shopping → Catalog | 0 | 12 | 0 | *ISellableItems* — a snapshot (id, default variant id, name, names by culture, image, price, sellable) |
| Notifications → Identity | 0 | 5 | 6 | *IAccountTokens* (issue and persist, return the raw token) plus *IStaffRecipients* for the two role lookups |
| Customers → Identity | 0 | 2 | 6 | *IAccountLifecycle* — rename, verify password, erase |
| Notifications → Ordering | 4 | 5 | 0 | *IOrderNotificationView*, or carry the fields on the event |
| Notifications → Platform | 6 | 0 | 0 | none needed — deliberate store-identity read |
| Customers → Shopping | 0 | 0 | 4 | *IForgetsCustomer*, implemented by baskets and wishlist, or an erasure event |
| Identity → Customers | 0 | 2 | 2 | *IAccountProfiles*, declared by Identity and **implemented by Customers** |
| Notifications → Customers | 0 | 4 | 0 | fold into *IOrderNotificationView* |
| Ordering → Customers | 0 | 4 | 0 | *ICustomerDirectory* — block status, address snapshot |
| Shopping → Promotions | 0 | 4 | 0 | *IDiscountQuote* (also needs Promotions added to Shopping's allowed contracts) |
| Notifications → Catalog | 0 | 2 | 0 | carry the product name on `StockBecameLow` |
| Reviews → Customers | 0 | 2 | 0 | *ICustomerDirectory*, shared with Ordering |
| Reviews → Platform | 2 | 0 | 0 | none — deliberate ([ModuleBoundaries.md](ModuleBoundaries.md) §1) |
| Notifications → Inventory | 1 | 0 | 0 | none — already a published event |
| Reviews → Ordering | 0 | 1 | 0 | *IOrderHistory* — "did this customer receive this product?" |

## The cycle, exactly

> **RESOLVED in M9.** Both contracts below were built, and the ratchet measured the result: **74 crossings across
> 15 pairs → 62 across 13**, with all twelve Identity↔Customers crossings gone and both pairs off the table.
> `ModuleAndContractRuleTests` now authorizes `["Customers"] = ["Identity"]` — one direction, no cycle possible.
> The paragraphs below are kept as the reasoning that produced the fix, and two notes worth carrying forward:
>
> - **The test suites had the same boundary violation as the code, and it had to move too.** Customers' tests
>   asserted `_user.Status`, `_user.PasswordHash` and `Forget(7)` *through* customer erasure, and Identity's tests
>   asserted the shape of the `Customer` aggregate through registration. Each module was pinning the other's
>   behaviour. `AccountLifecycleTests` and `CustomerAccountProfilesTests` now hold those assertions on the right
>   side, so coverage moved rather than shrank.
> - **`AllowedContracts` alone would not have caught a regression here.** The twelve crossings travelled through
>   `Souq.Domain.Interfaces` — repositories and aggregates — which that rule does not police. The guard that
>   counts is the generated `ModuleDomainDependencies.md`. Anyone re-opening this pair should watch that file,
>   not the contract map.

Identity ⇄ Customers is the one cycle the target graph forbids, and the only pair where reading the direction matters:

- **Identity → Customers:** `RegisterHandler` creates the `Customer` inside its own transaction (it needs `user.Id`); `AuthSessionIssuer` and `GetCurrentUserHandler` each read `FindIdByUserIdAsync` for the token's customer claim.
- **Customers → Identity:** `CustomerErasure` calls `User.Erase()` and revokes refresh tokens; `UpdateMyProfileHandler` calls `User.Rename(...)`; `EraseMyAccountHandler` reads `PasswordHash`.

The target graph keeps **Customers → Identity**, so the Identity → Customers arrow must disappear entirely — a contract in that direction would not break the cycle, it would formalise it. The minimum is two contracts, both **declared by Identity**:

1. *IAccountProfiles* — "an account was created", "which profile belongs to this account" — **implemented by Customers**. This is the same inversion already used for `IVariantStockInitializer` (declared by Catalog, implemented by Inventory), so it is a pattern the codebase already contains.
2. *IAccountLifecycle* — rename, verify password, erase — implemented by Identity, which also moves session invalidation back where it belongs.

## Ranked: what is worth fixing, and what it costs

| Rank | Fix | Crossings closed | Blast radius |
|---|---|---|---|
| 1 | ~~Break the Identity ⇄ Customers cycle~~ **DONE (M9)** | 12 (8 D) | ~13 files — the estimate held: 3 new source files, 6 rewired, DI, the contract map, and 4 test files |
| 2 | *IAccountTokens* — stop Notifications writing `User` | 9 (6 D) | ~7 files |
| 3 | ~~Move the payment-account use cases out of `Features/Stores`~~ **DONE (M1)** | 5 (2 D) | 6 files — see below, larger than first estimated |
| 4 | An erasure contract so Customers stops deleting baskets and wishlists | 4 (4 D) | ~7 files |
| 5 | *ISellableItems* — the largest cluster, all reads | 12 (0 D) | ~12 files |
| 6 | *IOrderNotificationView* plus the product name on the event | 10 (0 D) | ~6 files |
| 7 | *ICustomerDirectory*, shared by Ordering and Reviews | 6 (0 D) | ~8 files |
| 8 | *IDiscountQuote* | 4 (0 D) | ~7 files |
| 9 | *IOrderHistory* | 1 (0 D) | ~5 files |

**The trap in rank 3, and what it actually took.** `src/Souq.Application/Features/Platform/TenantPaymentAccounts.cs` depended on `StorePaymentAccountEditor` and its input and output types from `Features/Stores`. That compiled only because `ModuleMap` mapped **both** `Platform` and `Stores` to the module Platform. This entry originally said moving `StorePaymentAccounts.cs` alone to `Features/Payments` would fail `ModuleAndContractRuleTests` and that "both files must move together" — **that turned out to be incomplete.** Moving both files together was tried first, in M1, and it failed two different tests at once: `لا_طلب_من_العميل_يحمل_TenantId_خارج_منطقة_المنصّة` (the platform commands carry `TenantId`, and only `Features.Platform` may — `MultiTenancy.md` §2), and `وحدات_Application_لا_تشير_لبعضها_إلا_عبر_العقود_المسموحة` for `Payments` (which would then reach back into `Features.Platform` for `PlatformTenants.NotFound`). The two rules together mean **`TenantPaymentAccounts.cs` cannot leave `Features/Platform`.** The actual, correct fix: `Features/Payments/Contracts/StorePaymentAccountContracts.cs` publishes `IStorePaymentAccountEditor` plus the DTOs, `StorePaymentAccountEditor` (moved to `Features/Payments`) implements it, `TenantPaymentAccounts.cs` (staying in `Features/Platform`) calls only the interface through `ITenantScopeRunner`, and `AllowedContracts["Platform"] = ["Payments"]` in `ModuleAndContractRuleTests` authorizes exactly that crossing — turning five raw class-D/C references into one legitimate class-A contract call. **"No logic change" was the wrong estimate; this needed a genuine contract**, which is the accurate lesson for whoever reads this rank next: a "move the files" fix that spans a `TenantId`-carrying command should be checked against `MultiTenancy.md` §2 before it is called simple.

## Every crossing

**R/W** — whether the consumer only reads the other module's type, or also writes it. Order matches [ModuleDomainDependencies.md](ModuleDomainDependencies.md).

| # | From | To | Domain type | Used by | Class | R/W | Why |
|---|---|---|---|---|---|---|---|
| 1 | Identity | Customers | `Customer` | `RegisterHandler` | D | W | Identity creates the Customers aggregate |
| 2 | Identity | Customers | `ICustomerRepository` | `AuthSessionIssuer` | C | R | One id lookup for the token's customer claim |
| 3 | Identity | Customers | `ICustomerRepository` | `GetCurrentUserHandler` | C | R | The same lookup for the current-user response |
| 4 | Identity | Customers | `ICustomerRepository` | `RegisterHandler` | D | W | Adds the profile inside Identity's transaction |
| 5 | Customers | Identity | `IRefreshTokenRepository` | `CustomerErasure` | D | W | Lists and revokes Identity's sessions |
| 6 | Customers | Identity | `IUserRepository` | `CustomerErasure` | D | W | Loads the account in order to erase it |
| 7 | Customers | Identity | `IUserRepository` | `EraseMyAccountHandler` | C | R | Reads the hash to confirm the password |
| 8 | Customers | Identity | `IUserRepository` | `UpdateMyProfileHandler` | D | W | Loads the account in order to rename it |
| 9 | Customers | Identity | `RefreshToken` | `CustomerErasure` | D | W | Revokes each token |
| 10 | Customers | Identity | `User` | `CustomerErasure` | D | W | Erases identity, password hash and security stamp |
| 11 | Customers | Identity | `User` | `EraseMyAccountHandler` | C | R | Reads the hash only; the write is in `CustomerErasure` |
| 12 | Customers | Identity | `User` | `UpdateMyProfileHandler` | D | W | Renames the account so it follows the profile |
| 13 | Customers | Shopping | `Basket` | `CustomerErasure` | D | W | The loaded basket is deleted |
| 14 | Customers | Shopping | `IBasketRepository` | `CustomerErasure` | D | W | Loads and removes through Shopping's repository |
| 15 | Customers | Shopping | `IWishlistRepository` | `CustomerErasure` | D | W | Lists and removes each item |
| 16 | Customers | Shopping | `WishlistItem` | `CustomerErasure` | D | W | Each item is deleted by Customers |
| 17 | Shopping | Catalog | `CatalogTranslation` | `PricingService` | C | R | Name resolution by culture |
| 18 | Shopping | Catalog | `IProductRepository` | `AddBasketItemHandler` | C | R | Exists-and-published check |
| 19 | Shopping | Catalog | `IProductRepository` | `AddToWishlistHandler` | C | R | The same check |
| 20 | Shopping | Catalog | `IProductRepository` | `MergeWishlistHandler` | C | R | Batch check when merging a guest list |
| 21 | Shopping | Catalog | `IProductRepository` | `PricingService` | C | R | One batched load for live prices |
| 22 | Shopping | Catalog | `Product` | `AddBasketItemHandler` | C | R | Reads status, id, default variant id |
| 23 | Shopping | Catalog | `Product` | `AddToWishlistHandler` | C | R | Reads status |
| 24 | Shopping | Catalog | `Product` | `MergeWishlistHandler` | C | R | Reads status and id |
| 25 | Shopping | Catalog | `Product` | `PricingService` | C | R | Reads name, translations, image, price, status |
| 26 | Shopping | Catalog | `ProductTranslation` | `PricingService` | C | R | Builds the per-culture name map |
| 27 | Shopping | Catalog | `ProductVariant` | `AddBasketItemHandler` | C | R | The default variant id addresses the basket line |
| 28 | Shopping | Catalog | `ProductVariant` | `PricingService` | C | R | The default variant id on the quote line |
| 29 | Shopping | Promotions | `Coupon` | `PricingService` | C | R | `EnsureUsable` and `CalculateDiscount`, both non-mutating |
| 30 | Shopping | Promotions | `ICouponRedemptionRepository` | `PricingService` | C | R | Counts the customer's active uses |
| 31 | Shopping | Promotions | `ICouponRepository` | `PricingService` | C | R | Looks the code up |
| 32 | Shopping | Promotions | `InvalidCouponException` | `PricingService` | C | R | Caught and turned into a result code |
| 33 | Ordering | Customers | `Customer` | `CreateOrderHandler` | C | R | Reads block status and the address book |
| 34 | Ordering | Customers | `CustomerAddress` | `CreateOrderHandler` | C | R | Reads the default-billing flag |
| 35 | Ordering | Customers | `ICustomerRepository` | `CreateOrderHandler` | C | R | One load at the top of checkout |
| 36 | Ordering | Customers | `PostalAddress` | `CreateOrderHandler` | C | R | Snapshots the address and its country |
| 37 | Reviews | Customers | `Customer` | `CreateReviewHandler` | C | R | Reads block status |
| 38 | Reviews | Customers | `ICustomerRepository` | `CreateReviewHandler` | C | R | One load for that check |
| 39 | Reviews | Ordering | `IOrderRepository` | `CreateReviewHandler` | C | R | A purpose-built eligibility read |
| 40 | Reviews | Platform | `ITenantRepository` | `CreateReviewHandler` | B | R | Reads the store's live auto-approve policy |
| 41 | Reviews | Platform | `Tenant` | `CreateReviewHandler` | B | R | A store setting Platform owns by design |
| 42 | Notifications | Catalog | `IProductRepository` | `StockBecameLowHandler` | C | R | Names the product in the alert |
| 43 | Notifications | Catalog | `Product` | `StockBecameLowHandler` | C | R | Name in the store's default culture |
| 44 | Notifications | Customers | `Customer` | `OrderEmailHandler` | C | R | Email address and erasure check |
| 45 | Notifications | Customers | `Customer` | `OrderStatusChangedHandler` | C | R | Recipient account and erasure check |
| 46 | Notifications | Customers | `ICustomerRepository` | `OrderEmailHandler` | C | R | One load |
| 47 | Notifications | Customers | `ICustomerRepository` | `OrderStatusChangedHandler` | C | R | One load |
| 48 | Notifications | Identity | `IUserRepository` | `EmailVerificationEmailHandler` | D | W | Persists a new verification token hash |
| 49 | Notifications | Identity | `IUserRepository` | `InvitationEmailHandler` | D | W | Persists a renewed invitation token |
| 50 | Notifications | Identity | `IUserRepository` | `OrderStatusChangedHandler` | C | R | Staff recipients by role |
| 51 | Notifications | Identity | `IUserRepository` | `PasswordResetEmailHandler` | D | W | Persists a new reset token hash |
| 52 | Notifications | Identity | `IUserRepository` | `StockBecameLowHandler` | C | R | Staff recipients by role |
| 53 | Notifications | Identity | `User` | `EmailVerificationEmailHandler` | D | W | Generates the token on the aggregate |
| 54 | Notifications | Identity | `User` | `InvitationEmailHandler` | D | W | Renews the invitation on the aggregate |
| 55 | Notifications | Identity | `User` | `PasswordResetEmailHandler` | D | W | Generates the reset token on the aggregate |
| 56 | Notifications | Identity | `UserStatus` | `EmailVerificationEmailHandler` | C | R | Active-account guard |
| 57 | Notifications | Identity | `UserStatus` | `InvitationEmailHandler` | C | R | Pending-invitation guard |
| 58 | Notifications | Identity | `UserStatus` | `PasswordResetEmailHandler` | C | R | Active-account guard |
| 59 | Notifications | Inventory | `StockBecameLow` | `StockBecameLowHandler` | B | R | A published domain event through the outbox |
| 60 | Notifications | Ordering | `IOrderRepository` | `OrderEmailHandler` | C | R | Loads the order for the message body |
| 61 | Notifications | Ordering | `IOrderRepository` | `OrderStatusChangedHandler` | C | R | Loads the order number |
| 62 | Notifications | Ordering | `Order` | `OrderEmailHandler` | C | R | Number, total, currency, tracking, token |
| 63 | Notifications | Ordering | `Order` | `OrderStatusChangedHandler` | C | R | Id and number for the payload |
| 64 | Notifications | Ordering | `OrderActorKind` | `OrderStatusChangedHandler` | B | R | Arrives on the published event |
| 65 | Notifications | Ordering | `OrderStatus` | `OrderEmailHandler` | B | R | Arrives on the outbox message; selects the template |
| 66 | Notifications | Ordering | `OrderStatus` | `OrderStatusChangedHandler` | B | R | Arrives on the published event |
| 67 | Notifications | Ordering | `OrderStatusChanged` | `OrderStatusChangedHandler` | B | R | The published event itself |
| 68 | Notifications | Platform | `BrandColors` | `NotificationEmails` | B | R | Header colours |
| 69 | Notifications | Platform | `ITenantRepository` | `NotificationEmails` | B | R | Live store row so branding edits apply next message |
| 70 | Notifications | Platform | `StoreBranding` | `NotificationEmails` | B | R | Logo URL |
| 71 | Notifications | Platform | `StoreContact` | `NotificationEmails` | B | R | Reply-to address |
| 72 | Notifications | Platform | `StoreSettings` | `NotificationEmails` | B | R | Display name, branding, contact |
| 73 | Notifications | Platform | `Tenant` | `NotificationEmails` | B | R | Store identity, never written |
| 74 | Notifications | Ordering | `OrderItem` | `OrderEmailHandler` | C | R | **Added in Phase 17 by the R-06 fix**, and caught by the ratchet rather than slipping in. The order email is now itemised, so the handler reads each line's frozen name, quantity and total. Same character as rows 60–63: a read to compose a message, and part of the cluster *IOrderNotificationView* would close |

## What to do with this

- **The ratchet stands.** A new crossing changes [ModuleDomainDependencies.md](ModuleDomainDependencies.md) and fails `GeneratedDocsTests`, so it is a decision made in review. This page is what that reviewer should read first.
- **Do not chase the count.** Closing all 43 remaining class-C crossings would add contracts and a great deal of mapping for no behavioural gain. Ranks 1, 2 and 4 are where the remaining value is (rank 3 closed in M1): they are the class-D writes, and they are what makes Identity, Customers, Shopping and Notifications impossible to extract or reason about alone.
- **No crossing here is a runtime defect.** Tenant isolation, authorization and money safety do not depend on any of them; they are about who may change what, and what it costs to change it later.
