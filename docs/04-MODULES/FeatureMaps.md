# Feature maps: following a capability end to end

> **What this page is for:** answering "if I want to change X, where do I go?" without searching the whole repository. Each map follows one capability along the same chain:
> **frontend → endpoint → use case → domain → contracts → adapters → tables → events → tests**.
> **Companions:** [Endpoints.md](../05-API/Endpoints.md) (every endpoint → its use case) · [UseCases.md](UseCases.md) (every use case → its handler, validator and tests) · the module documents for the *why*.

## 1. Where each capability starts

| Capability | Entry point | Use case | Owning module |
|---|---|---|---|
| Browse and search the catalog | `GET /api/products` | `GetProductsQuery` | Catalog |
| Product detail | `GET /api/products/{id:int}` | `GetProductByIdQuery` | Catalog |
| Create a product | `POST /api/products` | `CreateProductCommand` | Catalog |
| Correct stock | `POST /api/admin/inventory/{productId:int}/adjustments` | `AdjustStockCommand` | Inventory |
| Basket and price quote | `GET /api/basket/quote` | `GetBasketQuery` | Shopping |
| Place an order | `POST /api/orders` | `CreateOrderCommand` | Ordering |
| Confirm payment (client) | `POST /api/orders/{id:int}/confirm-payment` | `ConfirmOrderPaymentCommand` | Ordering |
| Confirm payment (gateway) | `POST /api/payments/webhook` | `ProcessPaymentWebhookCommand` | Ordering → Payments |
| Refund | `POST /api/orders/{id:int}/refunds` | `RefundOrderCommand` | Payments |
| Sign in | `POST /api/auth/login` | `LoginCommand` | Identity |
| Reset a password | `POST /api/auth/forgot-password` | `ForgotPasswordCommand` | Identity → Notifications |
| Submit a review | `POST /api/products/{productId:int}/reviews` | `CreateReviewCommand` | Reviews |
| Moderate a review | `POST /api/admin/reviews/{id:int}/approve` | `ApproveReviewCommand` | Reviews |
| Wishlist | `PUT /api/wishlist/{productId:int}` | `AddToWishlistCommand` | Shopping |
| Erase an account | `POST /api/account/erase` | `EraseMyAccountCommand` | Customers |
| Store settings | `PUT /api/admin/store/settings` | `UpdateStoreSettingsCommand` | Platform |
| Storefront boot | `GET /api/storefront/config` | `GetStorefrontConfigQuery` | Platform |
| Create a store | `POST /api/platform/tenants` | `CreateTenantCommand` | Platform |
| Invite a store admin | `POST /api/platform/tenants/{id:int}/admins` | `InviteTenantAdminCommand` | Platform → Identity |

Everything else is one grep away in [Endpoints.md](../05-API/Endpoints.md).

---

## 2. Every request: which store is this?

Before any feature code runs, three decisions are already made. This is the map to read first, because it explains half of the "why did I get a 404?" questions.

| Step | Where | What happens |
|---|---|---|
| 1 | `frontend/src/app/TenantProvider.jsx` | The SPA calls `GET /api/storefront/config` for its own host and maps the answer: 503 → closed screen, 404 `StoreNotFound` → unknown host, another 404 → platform mode, network error → retry |
| 2 | `src/Souq.API/Program.cs` | Pipeline order: forwarded headers → correlation id → error contract → **tenant resolution** → static files → routing → **availability** → rate limits → CORS → authentication → request log scope → authorization → controllers |
| 3 | `src/Souq.API/Tenancy/TenantResolutionMiddleware.cs` | Host → store through `ITenantDirectory` (cached). Platform hosts become platform scope. In Development and Testing only: the `X-Tenant` header, `localhost` → the seeded store, `{slug}.localhost`. No match → 404 `StoreNotFound` |
| 4 | `src/Souq.API/Tenancy/TenantAvailability.cs` | Wrong area for this host → 404; store not active → 503 `StoreUnavailable`; `[RequiresModule]` off → 404 `ModuleDisabled` |
| 5 | `src/Souq.API/Security/AccessTokenValidation.cs` | The token's store claim must equal the resolved store (and be absent in platform scope); the session stamp is revalidated |
| 6 | `src/Souq.Infrastructure/Persistence/AppDbContext.cs` | The query filter restricts every read; the write guard stamps and checks every write |

**Tests:** `TenantResolutionMiddlewareTests`, `TenantResolutionTests`, `TenantIsolationTests`, `TenancyRuleTests`, `frontend/src/app/tenantModel.test.js`.
**Detail:** [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) · [Platform/README.md](Platform/README.md).

---

## 3. Checkout: basket → order → payment intent

The flow that touches the most modules, and the one to read before changing anything commercial.

| Step | Where | What happens |
|---|---|---|
| 1 | `frontend/src/pages/checkout/Checkout.jsx` | Prices the basket server-side (`GET /api/basket/quote`), then posts the address choice, coupon code and shipping method. **Never the items** — the server reads the basket |
| 2 | `OrdersController.Create` → `CreateOrderCommand` → `CreateOrderValidator` | Signed-in customer; shape checks only |
| 3 | `CreateOrderHandler`, before any write | Customer loaded and block-checked; addresses resolved from the address book; lines from the request or `IBasketCheckout`; **the same pricing pipeline the basket page used**; availability re-checked; coupon and shipping outcomes turned into error codes |
| 4 | Domain, in memory | `new Order(...)` (generates the tracking token, first history row), `AddItem` per priced line, `ApplyCoupon`, `ApplyShipping` |
| 5 | One transaction (`IUnitOfWork.InTransactionAsync`) | Order number from the store's counter → `AssignNumber` → `Place` (totals frozen) → save → `ICouponRedemptions.ReserveAsync` → `IInventoryReservations.ReserveAsync`. A shortage or an exhausted coupon here rolls the whole thing back |
| 6 | **Outside** the transaction | `IPaymentService.CreateIntentAsync` through `PaymentGatewayRouter` (store's own Stripe account, or the deployment's). A failure compensates: cancel the order, release the stock and the coupon, answer 503 |
| 7 | Second save | `SetPaymentIntent` + `IOrderPayments.RecordIntentAsync` |

**Writes:** `Orders`, `OrderItems`, `OrderStatusHistories`, `OrderNumberSequences`, `CouponRedemptions`, `Coupons`, `StockReservations`, `InventoryItems`, `Payments`.
**Events:** none — the first status is written before the order has an id, so checkout sends no notification. Staff learn about the order when it is **paid**.
**Tests:** `CreateOrderHandlerTests`, `OrderLifecycleTests` (both suites), `InventoryAndOrderTests` (no overselling under parallel checkouts), `CouponRedemptionTests` (concurrent single-use coupon), `ShippingTests`.
**Detail:** [Ordering/README.md](Ordering/README.md) · [Ordering/ChangeGuide.md](Ordering/ChangeGuide.md).

---

## 4. Pricing: one pipeline, two callers

| Step | Where | What happens |
|---|---|---|
| 1 | `BasketController.Quote` → `GetBasketQuery` → `BasketViews` | The basket page and checkout both call `IPricing.QuoteAsync` |
| 2 | `PricingService` | Store currency and modules from the tenant context; lines priced from the **live** catalog (`IProductRepository`), unsellable lines marked |
| 3 | Discount | Module flag → coupon lookup → per-customer uses → `Coupon.EnsureUsable` → `Coupon.CalculateDiscount`. A rejection becomes an *outcome* (200 with a code in the basket) at quote time and a 422 at checkout |
| 4 | Shipping | `IShippingRateProvider` → `StoreShippingRates`: active methods in the store currency serving the destination country, priced with the free-over threshold **after** the discount |
| 5 | Tax | An explicit zero — open decision **P-06** |
| 6 | Total | goods + shipping + tax, as `Money` |

**Why it matters:** the basket total and the order total are equal by construction because there is exactly one pipeline.
**Tests:** `PricingServiceTests`, `StoreShippingRatesTests`, `BasketTests`, `ShippingTests`, `CouponRedemptionTests`.
**Detail:** [Shopping/README.md](Shopping/README.md) · [Promotions/README.md](Promotions/README.md) · [Shipping/README.md](Shipping/README.md).

---

## 5. Payment confirmation: two doors, one room

| Path | Proves | Route |
|---|---|---|
| Client | ownership (`[Authorize]` + owner or `orders.manage`) | `POST /api/orders/{id}/confirm-payment` |
| Webhook | a gateway signature | `POST /api/payments/webhook` |

Both end in `OrderPaymentConfirmation.ConfirmAsync`:

1. **Idempotency first** — an order that is no longer Pending returns its current status without calling the gateway.
2. **The gateway is asked** (outside any transaction) through the account recorded on the payment. Only `succeeded` counts; anything else cancels the order, releases stock and the coupon use, and answers `PaymentFailed`.
3. **On success, one transaction:** `MarkAsPaid` (transition table, history row, `OrderStatusChanged` raised) → coupon redemption confirmed → payment marked succeeded → the purchased quantities leave the basket → save → `IInventoryReservations.CommitAsync` (the only place a Sale movement is written, and where `StockBecameLow` may fire).
4. **The race** between the two doors is resolved on the order's `rowversion`: the loser re-reads, sees a settled order, and returns success — no second email, no second commit.

The webhook additionally routes by the intent's metadata: an event for another store, signed by the deployment account, is re-dispatched **inside that store's scope**; signed by a store's own account, it is ignored.

**Tests:** `ConfirmOrderPaymentHandlerTests`, `ProcessPaymentWebhookHandlerTests`, `PaymentsAndRefundsTests`, `NotificationTests`, `AuthorizationBoundaryTests`.
**Detail:** [Payments/README.md](Payments/README.md) · [Ordering/ChangeGuide.md](Ordering/ChangeGuide.md).

---

## 6. Anything that leaves after the commit: the outbox

| Step | Where | What happens |
|---|---|---|
| 1 | A use case, or an aggregate | Either `INotificationOutbox.Enqueue(message)` (reset, verification, invitation, order email) or a domain event raised by an aggregate (`OrderStatusChanged`, `StockBecameLow`) |
| 2 | `AppDbContext.SaveChangesAsync` | Domain events become outbox rows **in the same transaction**; a failed save detaches them |
| 3 | `OutboxDispatcherService` | Every 5 seconds (0 disables it, which is what tests use); claims up to 50 due rows under a 2-minute lease |
| 4 | `OutboxProcessor` | Runs the handler inside the row's **store scope**, outside any transaction |
| 5 | Handlers (Notifications) | Write in-app rows, issue tokens *at dispatch*, then call the provider |
| 6 | Outcome | Success marks the row processed; failure retries on a bounded schedule (30s → 6h, 8 attempts) then dies with its error kept. Processed rows are purged after 14 days |

**Why:** no request waits on a provider, and nothing is sent for a rolled-back change.
**Tests:** `OutboxPolicyTests`, `IdentityEmailHandlersTests`, `DomainEventTests`, `NotificationTests`.
**Detail:** [Events.md](../02-ARCHITECTURE/Events.md) · [Notifications/README.md](Notifications/README.md).

---

## 7. The rest, in one line each

| Capability | The chain | Detail |
|---|---|---|
| **Product creation** | `POST /api/products` → `CreateProductCommand` → `Product` with exactly one default variant → Catalog's own `IVariantStockInitializer`, implemented by Inventory, opens stock → `Products`, `ProductVariants`, `ProductTranslations`, `InventoryItems` | [Catalog/README.md](Catalog/README.md) |
| **Stock adjustment** | `POST /api/admin/inventory/{id}/adjustments` → `AdjustStockCommand` → `InventoryItem.Adjust` (delta + reason, never an absolute set) → one `StockMovements` row, possibly `StockBecameLow` | [Inventory/README.md](Inventory/README.md) |
| **Reservation lifecycle** | checkout reserves → payment commits (Sale movement) → cancellation or the expiry sweep releases → every step writes the ledger | [Inventory/ChangeGuide.md](Inventory/ChangeGuide.md) |
| **Sign-in and refresh** | `POST /api/auth/login` → `LoginCommand` → lockout and status checks → access token (store bound) + rotating refresh cookie; `POST /api/auth/refresh` rotates and detects reuse | [Identity/README.md](Identity/README.md) |
| **Password reset** | `POST /api/auth/forgot-password` → enqueue `PasswordResetRequested` → the handler issues the token **at dispatch**, stores its hash, sends the branded email | [Identity/ChangeGuide.md](Identity/ChangeGuide.md) |
| **Store provisioning** | `POST /api/platform/tenants` → domain → modules → `InviteTenantAdminCommand` (runs inside the new store's scope) → activation. Every step audited | [Platform/ChangeGuide.md](Platform/ChangeGuide.md) |
| **Store settings → storefront** | `PUT /api/admin/store/settings` → validation (including contrast) → cache invalidation → `GET /api/storefront/config` with an ETag → the SPA re-themes | [Platform/README.md](Platform/README.md) |
| **Review and moderation** | `POST /api/products/{id}/reviews` → verified-purchase check → the store's auto-approve policy decides Pending or Approved → `POST /api/admin/reviews/{id}/approve` (audited) → aggregates count approved reviews only | [Reviews/README.md](Reviews/README.md) |
| **Wishlist** | `PUT /api/wishlist/{productId}` behind the `wishlist` module flag; the guest list merges at sign-in; prices come live from the catalog | [Shopping/README.md](Shopping/README.md) |
| **Erasure and export** | `POST /api/account/erase` → `CustomerErasure` anonymizes in place, revokes sessions, deletes basket and wishlist, **keeps orders** | [Customers/README.md](Customers/README.md) |
| **Refund** | `POST /api/orders/{id}/refunds` → reserve → call the gateway → record, with an idempotency key; a retry is safe | [Payments/ChangeGuide.md](Payments/ChangeGuide.md) |
| **Checkout expiry** | a hosted service asks Ordering to settle abandoned checkouts: cancel the order, release stock and the coupon | [Ordering/ChangeGuide.md](Ordering/ChangeGuide.md) |

## 8. Using these maps

- **Changing behaviour?** Find the capability, read the module document, then its change guide.
- **Debugging?** Start from the error `code`, find the endpoint in [Endpoints.md](../05-API/Endpoints.md), then the use case in [UseCases.md](UseCases.md), then the handler.
- **Adding a capability?** Follow the same chain in [HowToAddAFeature.md](../00-START-HERE/HowToAddAFeature.md), and add a row here when it is worth following end to end.
