# Shopping module

> **Code:** `src/Souq.Application/Features/Baskets`, `src/Souq.Application/Features/Wishlist`, `src/Souq.Domain/Entities/Basket.cs`, `src/Souq.Domain/Entities/WishlistItem.cs`, `src/Souq.API/Controllers/BasketController.cs`, `src/Souq.API/Controllers/WishlistController.cs` · **Decisions:** [ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md), [ADR-0033](../../11-ADR/0033-review-moderation-and-wishlist.md) (wishlist), [ADR-0026](../../11-ADR/0026-inventory-reservations.md) (baskets reserve nothing) · **Change guide:** [ChangeGuide.md](ChangeGuide.md)

## Purpose

Shopping owns what a shopper intends to buy (the basket) or wants to remember (the wishlist), and it owns the single pricing pipeline that turns lines into money. Pre-purchase intent has different rules from a commercial record: nothing is financial, prices are always re-read from the catalog, and both guests and customers use it. Basket and wishlist share one module because they share the same lifecycle — a guest collects, signs in, and the guest's collection is absorbed into the account ([ModuleBoundaries.md](../../02-ARCHITECTURE/ModuleBoundaries.md), [Modules.md](../Modules.md)).

## Responsibilities

- A server-side basket for guests (cookie token) and customers (session), the merge at sign-in, sliding expiry and the purge of expired baskets.
- One pricing pipeline, `IPricing`: subtotal → discount → shipping → tax → total, used by the basket view and by checkout.
- Handing checkout the customer's basket lines and consuming purchased quantities when payment is confirmed (`IBasketCheckout`).
- A server-side wishlist for customers, and the merge of the guest's browser list.
- Reporting availability per basket line (read-only) and summarising whether the basket would pass checkout.

## Not this module's job

| Concern | Owner |
|---|---|
| Product data, prices, publication status | Catalog — Shopping re-reads them on every quote |
| Stock levels and reservations | Inventory — only checkout reserves (`IInventoryReservations`) |
| Coupon rules, usage counting, redemption records | Promotions — the pipeline applies the `Coupon` entity's rules, it does not own them |
| Shipping methods and rates | Shipping (`IShippingRateProvider`) |
| Creating the order, freezing totals, taking payment | Ordering and Payments |
| Customer profile, address book, erasure orchestration | Customers (`CustomerErasure` deletes the basket and wishlist) |
| Module flags | Platform |

## Business concepts

- **Basket:** one owner's purchase intent in one store; lines of variant plus quantity, never a price.
- **Guest basket:** a basket owned by an anonymous visitor, identified by a random cookie token whose hash is stored.
- **Merge:** folding a guest basket into the customer's basket on the first basket request after sign-in.
- **Sliding expiry:** the lifetime restarts at every change; expired baskets read as empty and are purged.
- **Quote:** the basket priced now — lines, subtotal, discount, shipping, tax, total, coupon outcome, shipping options.
- **Ready for checkout:** every line sellable and within available stock, and any requested coupon accepted.
- **Wishlist:** products a customer saved, at most 200, always shown with live catalog data.
- **Guest wishlist:** the visitor's browser list, merged into the account at sign-in.

## Domain model

| Type | Kind | Path | Invariants it guards |
|---|---|---|---|
| `Basket` | aggregate root | `src/Souq.Domain/Entities/Basket.cs` | Exactly one owner (a customer id > 0, or a 64-character token hash); at most `Basket.MaxLines` (50) lines; 1–`Basket.MaxQuantityPerLine` (99) per line; an explicit add over the limit is rejected, never trimmed; quantity 0 removes a line; `MergeFrom` accepts only guest → customer, sums quantities capped at 99 and drops lines beyond 50; every change slides `ExpiresAt` |
| `BasketLine` | entity in the `Basket` aggregate | same file | Product, variant and quantity; no price. Only `Basket` constructs or changes it (`internal`) |
| `WishlistItem` | aggregate root (one row) | `src/Souq.Domain/Entities/WishlistItem.cs` | Customer and product ids > 0; `WishlistItem.HasRoom` caps a customer at `WishlistItem.MaxItemsPerCustomer` (200) |
| `InvalidBasketOperationException` | domain exception | `src/Souq.Domain/Exceptions/BasketExceptions.cs` | Stable code `InvalidBasketOperation` |
| `Money` | value object (shared kernel) | `src/Souq.Domain/ValueObjects/Money.cs` | Every amount in the pipeline; rounding once through `Money.FromCalculation` ([ADR-0014](../../11-ADR/0014-money-precision.md)) |

Notes:

- **Aggregate boundaries.** `Basket` owns its lines (cascade delete). `WishlistItem` stands alone; "at most 200" is checked in the handler through `HasRoom`, and the unique index is the last guard.
- **Concurrency.** Neither table has a `rowversion`. The last write wins in a basket — a 409 on a cart edit is noise for a record that is not financial, and checkout re-validates everything ([ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md)). Filtered unique indexes prevent two baskets per owner; the unique wishlist index prevents duplicates.
- **Lifecycle.** No state machine: a basket lives while `ExpiresAt` is in the future. A customer's expired basket is emptied and its expiry reset on the next access; a guest's expired basket is deleted.
- `WishlistItem` throws Customers' `InvalidCustomerDataException` (code `InvalidCustomerData`) rather than a Shopping exception.

## How it works

### The pricing pipeline

`PricingService` (`src/Souq.Application/Features/Baskets/Pricing/PricingService.cs`) implements `IPricing.QuoteAsync(lines, couponCode, customerId, shipping)`. Every amount is `Money` in the store currency, taken from `ITenantContext.RequireTenant()`.

| # | Stage | What happens |
|---|---|---|
| 1 | Lines | All products load in one call (`IProductRepository.GetManyAsync`, tenant-filtered, so another store's product is simply missing). Each line is priced at the product's live price and default variant, with the name in the store's default culture plus every translation for display. A missing or unpublished product becomes a line with `Sellable = false` and zero amounts |
| 2 | Subtotal | The sum of sellable line totals only |
| 3 | Discount | Only when a code is given. `promotions` disabled ⇒ outcome `ModuleDisabled`; unknown code ⇒ `CouponNotFound`; otherwise `Coupon.EnsureUsable(subtotal, now, customerUses)`, where `customerUses` comes from `ICouponRedemptionRepository.CountActiveAsync` when a customer id is known and is 0 for guests; a rule failure ⇒ `InvalidCoupon` with the entity's message. Accepted ⇒ `Coupon.CalculateDiscount(subtotal)` |
| 4 | Shipping | `goods = subtotal − discount`, then `IShippingRateProvider.QuoteAsync(goods, country)`. Options are priced against `goods`, so the free-shipping threshold is measured **after** the discount — otherwise a coupon could pull an order under the threshold and still ship free ([ADR-0032](../../11-ADR/0032-shipping-methods.md)). The chosen option sets the cost; problems are outcomes: `ShippingMethodUnavailable`, `ShippingNotAvailable`, `ShippingMethodRequired`. A store with no active methods ships free with no choice |
| 5 | Tax | An explicit zero. There is no tax model and no phase plans one; it is open product decision **P-06**. The stage keeps its fixed place in `PriceQuote` and `BasketDto`, so introducing tax changes no contract |
| 6 | Total | `goods + shipping + tax` |

A coupon or shipping problem never fails the quote; it is reported inside `CouponOutcome` and `ShippingOutcome` so the basket stays visible with a message in its place. Checkout turns the same outcomes into `422` responses before any write.

**Exactly two callers:**

- `BasketViews.BuildAsync` — for every basket response (`GET /api/basket`, `GET /api/basket/quote` and each write). It passes `ICurrentUser.CustomerId`, which is null for guests and staff.
- `CreateOrderHandler` (Ordering) — with the request's lines or the customer's basket lines (`IBasketCheckout.LinesForCustomerAsync`), the coupon code, the customer id, and a `ShippingRequest` whose country comes from the customer's own address book.

**Why exactly one pipeline.** Before Phase 8 money was computed in three places — the browser cart, checkout, and the coupon preview — and they could disagree. With one pipeline the basket total equals the checkout total by construction; the integration test `BasketTests` places an order from the basket's lines and coupon and compares subtotal, discount and total. One evaluator remains outside it: the legacy `GET /api/coupons/apply` preview (Promotions), which the SPA no longer calls.

`ReadyForCheckout` (in `BasketViews`) is true when there is at least one line, every line is sellable and within its available stock, and a requested coupon was accepted. It does **not** consider the shipping outcome; the checkout page checks that separately with `shippingProblem` in `frontend/src/features/checkout/shippingOptions.js`.

### Guest baskets, merge and expiry

- **Token.** `GuestBasketTokens.New()` produces 32 random bytes in Base64Url (43 characters). Only `GuestBasketTokens.Hash` (SHA-256, lower-case hex, 64 characters) reaches the database, so a leaked database exposes no usable token.
- **Cookie.** `BasketController` writes `souq_basket` (`BasketController.GuestCookieName`): `HttpOnly`, `SameSite=Strict`, `Path=/api/basket`, `Secure` taken from `Auth:RefreshCookie:Secure`, expiring with the basket, flagged essential. Scripts cannot read it and it is sent to no other endpoint.
- **Creation.** Only a write by a caller without a customer profile creates a guest basket (`BasketResolver.EnsureAsync`). Reads never create a basket or a cookie.
- **Resolution** (`BasketResolver.ResolveAsync`). A malformed token counts as absent. The hash is looked up in the host's store only, so a token from another store, an expired basket or one already merged matches nothing and the response deletes the cookie. An expired guest basket found by its token is removed on the next save.
- **Merge at sign-in.** A customer request that still carries a live guest token folds that basket into the customer's (creating one if needed) with `Basket.MergeFrom`, deletes the guest basket and clears the cookie. It happens on the first basket request, not at login, because the cookie is scoped to `/api/basket` and never reaches `/api/auth`. `CartContext` re-reads the basket whenever the signed-in user changes, which triggers it. `GetBasketHandler` therefore saves: this GET can write.
- **Staff accounts** have no purchase profile, so they get a guest basket like any visitor — a 403 would break the storefront for staff who browse it.
- **Sliding expiry.** `Add`, `SetQuantity`, `Remove`, `Clear` and `MergeFrom` all set `ExpiresAt = now + lifetime`; reads do not. Lifetimes come from `Basket:GuestLifetimeDays` (default 30, accepted 1–365) and `Basket:CustomerLifetimeDays` (default 180, accepted 1–730), validated at startup in `src/Souq.Infrastructure/DependencyInjection.cs`. After a guest write the cookie is re-issued with the new expiry.
- **Purge.** `BasketCleanupService` (a `StoreSweepService`) runs every `Basket:CleanupIntervalMinutes` (default 60, `0` disables it, otherwise 5–1440) and sends `PurgeExpiredBasketsCommand` — up to 500 baskets per store per run, oldest first — inside each active store's scope. Lines cascade.
- **Checkout.** `IBasketCheckout.LinesForCustomerAsync` treats an expired basket as empty (checkout answers `400 BasketEmpty`). `IBasketCheckout.ConsumeAsync` subtracts the purchased quantities in the payment-confirmation transaction; anything added after the order was placed stays.

### Why baskets never reserve stock

Reserving on add lets abandoned carts lock stock and lets a bot empty a store, so baskets reserve nothing ([ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md), closing the question [ADR-0026](../../11-ADR/0026-inventory-reservations.md) left open). The basket only reads `IStockAvailability`:

- adding, or increasing a quantity, beyond what is available now fails with `422 InsufficientStock` — an early, clear message, not a guarantee;
- decreasing is always allowed;
- each line carries `Available`; a line whose stock later fell below its quantity, or whose product was archived, stays visible, flagged, and makes `ReadyForCheckout` false. Nothing disappears silently.

Checkout re-checks availability and reserves atomically inside the order transaction.

### Wishlist

- Rows are `WishlistItem` (customer, product) inside the store. The customer is always the caller (`ICurrentUser.RequireCustomerId`), so no customer id appears in any route.
- **Add** (`PUT /api/wishlist/{productId}`) is idempotent: a product already saved returns success with no write — two clicks on the heart do not duplicate. The product must belong to this store and be published, else 404. A full list gives `422 WishlistFull`. A race that slips past the count hits the unique index (`409 DuplicateValue`).
- **Every operation returns the whole list** through `IWishlistQueries` (`WishlistQueries` in Infrastructure): newest first, name in the store's default culture with all translations, the live default-variant price and compare-at price, available stock (on hand minus reserved), and the first image. Products that are not published, or whose category is disabled, are hidden rather than deleted and reappear when republished.
- **Guest list.** It stays in the browser (`localStorage`, key `souq_wishlist`). At sign-in `WishlistContext` sends the local ids — `localIds` keeps distinct positive integers, newest 200 — to `POST /api/wishlist/merge`, then clears the local copy. `MergeWishlistHandler` keeps the guest's order, and silently skips ids already saved, unknown, from another store, unpublished, or beyond the cap; it saves only if something was added, so a stale local list never breaks sign-in.
- Staff and admin accounts, and stores with the module off, keep using the local list (`serverUnavailable` treats `ModuleDisabled` and 403 as "no server wishlist").

### Module flags

- **`wishlist`** (`StoreModules.Wishlist`). `WishlistController` carries `[RequiresModule(StoreModules.Wishlist)]`, so `TenantAvailabilityMiddleware` answers `404 ModuleDisabled` on every wishlist route before authentication and before any handler runs. The handlers themselves do not check the flag. The SPA hides the heart, the menu entries and the `/wishlist` route (`useModule('wishlist')`, `RequireModule`).
- **`promotions`** (`StoreModules.Promotions`). Checked inside the pipeline (stage 3), so the quote reports `ModuleDisabled` and checkout rejects with `422 ModuleDisabled`; `AddressStep` hides the coupon field.
- The basket has no flag: every store has a basket.
- Flags come from the cached tenant snapshot (`TenantInfo.HasModule`; a snapshot built without modules means "all enabled"). A change applies immediately on the instance that made it and within 60 seconds elsewhere (`TenantDirectoryCache`).

## Use cases

| Use case | Command or query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| Read the basket (merges a guest basket after sign-in) | `GetBasketQuery` | `GetBasketHandler` | anonymous or customer | `GET /api/basket` |
| Quote with a coupon and a shipping method | `GetBasketQuery` | `GetBasketHandler` | anonymous or customer | `GET /api/basket/quote` |
| Add a product | `AddBasketItemCommand` | `AddBasketItemHandler` | anonymous or customer | `POST /api/basket/items` |
| Set a line's quantity (0 removes it) | `SetBasketItemQuantityCommand` | `SetBasketItemQuantityHandler` | anonymous or customer | `PUT /api/basket/items/{productId}` |
| Remove a line | `RemoveBasketItemCommand` | `RemoveBasketItemHandler` | anonymous or customer | `DELETE /api/basket/items/{productId}` |
| Empty the basket | `ClearBasketCommand` | `ClearBasketHandler` | anonymous or customer | `DELETE /api/basket` |
| Purge expired baskets | `PurgeExpiredBasketsCommand` | `PurgeExpiredBasketsHandler` | system (`BasketCleanupService`) | — |
| Read the wishlist | `GetWishlistQuery` | `GetWishlistHandler` | customer | `GET /api/wishlist` |
| Add to the wishlist | `AddToWishlistCommand` | `AddToWishlistHandler` | customer | `PUT /api/wishlist/{productId}` |
| Remove from the wishlist | `RemoveFromWishlistCommand` | `RemoveFromWishlistHandler` | customer | `DELETE /api/wishlist/{productId}` |
| Merge the guest list | `MergeWishlistCommand` | `MergeWishlistHandler` | customer | `POST /api/wishlist/merge` |

Validators: `GetBasketValidator` (coupon ≤ 50 characters, method id > 0, country two letters), `AddBasketItemValidator` (quantity 1–99), `SetBasketItemQuantityValidator` (0–99), `MergeWishlistValidator` (at most 200 ids, each > 0). Removing and clearing have no validator.

**Frontend.** The basket is held in `frontend/src/context/CartContext.jsx` and shown in the cart drawer (`frontend/src/components/cart/CartDrawer.jsx`, with `CartLine.jsx` and `CartSummary.jsx`) and on the `/cart` page (`frontend/src/pages/Cart.jsx`); checkout (`frontend/src/pages/checkout/Checkout.jsx`) asks `GET /api/basket/quote` for the priced total. The wishlist is `frontend/src/context/WishlistContext.jsx` and the `/wishlist` page (`frontend/src/pages/Wishlist.jsx`, behind the `wishlist` module). Pure basket and wishlist rules are in `frontend/src/features/basket/basketModel.js` and `frontend/src/features/wishlist/wishlistModel.js`.

## Public contracts

| Contract | Path | Implementation | Callers |
|---|---|---|---|
| `IPricing` with `PricingLine`, `ShippingRequest`, `PriceQuote`, `PricedLine`, `CouponOutcome`, `ShippingOutcome` | `src/Souq.Application/Features/Baskets/Contracts/PricingContracts.cs` | `PricingService` | `BasketViews` (this module) and `CreateOrderHandler` (Ordering) |
| `IBasketCheckout` | `src/Souq.Application/Features/Baskets/Contracts/BasketCheckoutContracts.cs` | `BasketCheckout` | `CreateOrderHandler` (`LinesForCustomerAsync`), `OrderPaymentConfirmation` (`ConsumeAsync`) |

- `IBasketCheckout.ConsumeAsync` deliberately does not save: the caller's unit of work commits it together with the payment confirmation, so the basket empties only if the payment is recorded, and a failed or cancelled order leaves the basket untouched.
- `IWishlistQueries` (declared in `src/Souq.Application/Features/Wishlist/WishlistUseCases.cs`) is this module's own read port, implemented by `WishlistQueries` in Infrastructure ([ADR-0008](../../11-ADR/0008-cqrs-strategy.md)).
- Earlier module documentation named the basket contract *IBasketReader*; the contract that shipped is `IBasketCheckout`.

## Dependencies

- **Uses (contracts, allowed by the architecture test):** Inventory's `IStockAvailability` (availability for display and for the early stock check) and Shipping's `IShippingRateProvider` (pipeline stage 4).
- **Uses (shared kernel):** `Money`, `ICurrentUser`, `ITenantContext` / `TenantInfo`, `StoreModules`, `IUnitOfWork`, `Result` / `Error`, `TimeProvider`.
- **Boundary leaks — other modules' domain types, which no test catches:**
  - `PricingService` uses Catalog's `IProductRepository` and `Product`, and Promotions' `ICouponRepository`, `ICouponRedemptionRepository`, `Coupon` and `InvalidCouponException`.
  - `AddBasketItemHandler`, `AddToWishlistHandler` and `MergeWishlistHandler` use `IProductRepository` and `Product.IsActive`.
  - `WishlistQueries` (Infrastructure) joins `Products` with their category, variants, images and translations, and reads `InventoryItems`.
  - `WishlistItem` throws Customers' `InvalidCustomerDataException`.
- **Leaks into Shopping:** `CustomerErasure` (Customers) uses `IBasketRepository` and `IWishlistRepository`; `CreateOrderValidator` (Ordering) uses `Basket.MaxLines`.
- **Used by:** Ordering (`IPricing`, `IBasketCheckout`) and Customers (erasure, through the repositories above).
- **Enforced vs convention.** `ModuleAndContractRuleTests` maps Shopping to the `Baskets` and `Wishlist` folders, allows Shopping → Inventory and Shipping **contracts**, and allows Ordering → Shopping contracts; any other reference between feature folders fails the build. It does not inspect `Souq.Domain.Interfaces` or `Souq.Domain.Entities`, so every leak above passes. [Modules.md](../Modules.md) draws Shopping → Catalog and Shopping → Promotions as dependencies; today they are domain-repository calls, not contracts. `TenancyRuleTests` enforces that `Basket`, `BasketLine` and `WishlistItem` are tenant-owned with tenant-carrying foreign keys.

## Data ownership

| Table | EF configuration | Tenant-owned | Concurrency | Constraints and indexes that encode rules |
|---|---|---|---|---|
| `Baskets` | `BasketConfiguration` | yes | none (last write wins) | `CK_Baskets_Owner`: exactly one of `CustomerId` / `GuestTokenHash`; filtered unique `(TenantId, CustomerId)` and `(TenantId, GuestTokenHash)`; `(TenantId, ExpiresAt)` for the purge; customer FK with Restrict (erasure deletes the basket explicitly) |
| `BasketLines` | `BasketLineConfiguration` (same file) | yes | none | `CK_BasketLines_Quantity` (1–99); unique `(BasketId, VariantId)`; composite FKs to product and variant in the same store, Restrict because products are archived rather than deleted; cascade from the basket |
| `WishlistItems` | `WishlistItemConfiguration` | yes | none | unique `(TenantId, CustomerId, ProductId)`; composite FKs to customer and product, both Restrict |

- Migrations: `Phase8Basket` (baskets) and `Phase13ReviewsWishlist` (wishlist), both additive.
- **Other modules' data it reads:** products through Catalog's repository; product, category, variant, image and `InventoryItems` rows inside `WishlistQueries`; `Coupons` and `CouponRedemptions` through Promotions' repositories; `ShippingMethods` through `IShippingRateProvider`; stock through `IStockAvailability`.
- **Nothing is snapshotted.** Prices, names and images are read live at every quote; the order takes its own snapshot at placement.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/basket` | anonymous (guest cookie or customer session) | — | read |
| GET | `/api/basket/quote?couponCode=&shippingMethodId=&country=` | anonymous; `coupon-preview` rate limit | — (`promotions` checked in the pipeline) | quote |
| POST | `/api/basket/items` | anonymous; `basket` rate limit | — | add |
| PUT | `/api/basket/items/{productId}` | anonymous; `basket` rate limit | — | set quantity |
| DELETE | `/api/basket/items/{productId}` | anonymous; `basket` rate limit | — | remove |
| DELETE | `/api/basket` | anonymous; `basket` rate limit; answers 204 | — | clear |
| GET | `/api/wishlist` | authenticated customer | `wishlist` | read |
| PUT | `/api/wishlist/{productId}` | authenticated customer | `wishlist` | add (idempotent) |
| DELETE | `/api/wishlist/{productId}` | authenticated customer | `wishlist` | remove |
| POST | `/api/wishlist/merge` | authenticated customer | `wishlist` | merge the guest list |

Every basket response is the whole `BasketDto` (lines with `sellable` and `available`, `subtotal`, `discount`, `shipping`, `tax`, `total`, `coupon`, `readyForCheckout`, `expiresAt`, `shippingMethods`); every wishlist response is the whole `WishlistDto`. Frontend calls live in `frontend/src/api/client.js`: `getBasket`, `quoteBasket`, `addToBasket`, `setBasketQuantity`, `removeFromBasket`, `clearBasket`, `getWishlist`, `addToWishlist`, `removeFromWishlist`, `mergeWishlist`.

## Security and permissions

- Basket routes are anonymous by design and appear in the reviewed public list in `AuthorizationBoundaryTests`. Every request acts only on the caller's own basket — the customer's, or the one the guest token opens in the host's store. No basket id appears in any route.
- The guest token is 256 bits of randomness, HttpOnly, `SameSite=Strict` and path-scoped, and only its hash is stored.
- Rate limits are fixed windows per host plus client address: writes use `basket` (`RateLimiting:Basket`, 120 per 60 s by default) because each first add by a new visitor creates a row; quotes share `coupon-preview` (`RateLimiting:CouponPreview`, 30 per 60 s) because the quote is the only place a coupon code can be tried. Plain `GET /api/basket` never evaluates a coupon.
- Wishlist routes require `[Authorize]`, and the handlers call `RequireCustomerId`: an anonymous caller gets 401, a staff or admin account gets `403 CustomerAccountRequired`.
- No permission is involved anywhere in this module; there is no admin surface for baskets or wishlists.
- Erasure ([ADR-0027](../../11-ADR/0027-customer-profile-and-erasure.md)): `CustomerErasure` deletes the customer's basket and wishlist, because both are intent rather than record.

## Tenant behaviour

- All three entities are `ITenantOwned`: the named query filter scopes every read, the write guard stamps the store, and composite foreign keys keep lines and wishlist rows pointing at the same store's products and variants.
- A store-A guest token presented on store B's host matches nothing: the response is an empty basket and a deleted cookie, and store A's basket is untouched (`TenantIsolationTests`). Adding store A's product on store B is a 404, and the wishlist merge ignores another store's products.
- Amounts use the store currency; the pipeline starts from `Money.Zero(store.Currency)` and names come from the store's default culture.
- The purge runs per active store, inside that store's scope, so the tenant filter and write guard behave exactly as in an HTTP request.

## Events and background work

- `Basket` and `WishlistItem` raise no domain events, so nothing from this module reaches the outbox.
- `BasketCleanupService` is the module's only hosted service (see *Guest baskets, merge and expiry*). With several API instances each one sweeps; deletes are idempotent, so this is safe but duplicated. A distributed lock is left to the Phase 23 production-readiness review, per the comment in `StoreSweepService`.
- Consuming the basket at payment runs inside Ordering's confirmation transaction, not as an event.

## External integrations

None. Nothing in this module calls a network service.

## Tests

| Layer | Class | What it covers |
|---|---|---|
| Domain | `BasketTests` | merge of a variant's line and the expiry slide; quantity bounds; no silent trimming; the 50-line limit; zero removes and an absent line is refused; merge sums, caps and adds; merge direction; owner validity; clear |
| Domain | `WishlistItemTests` | owner guards; the 200 cap |
| Domain | `DomainExceptionCodeTests` | the `InvalidBasketOperation` code |
| Application | `PricingServiceTests` | live catalog prices with shipping and tax at zero; unsellable and missing lines excluded and flagged; coupon accepted; coupon problems as outcomes including `ModuleDisabled`; per-customer limit only for a known customer; the chosen method priced after the discount; the three shipping problems; stores without methods |
| Application | `BasketHandlersTests` | a new guest gets a token and only its hash is stored; unavailable product and over-availability rejected without creating a basket; merge at sign-in, including into a brand-new customer basket; expired or malformed tokens treated as absent and cleared; staff use a guest basket; only increases are measured against stock; the purge |
| Application | `BasketCheckoutTests` | basket lines for checkout and an expired basket read as empty; only purchased quantities are consumed |
| Application | `WishlistHandlersTests` | idempotent add; 404 for unpublished or foreign products; the cap; removal; merge order, skips and cap; guests and staff refused; the merge validator |
| Integration | `BasketTests` | cookie flags and live pricing; stock limits without reservation and archived lines; merge at sign-in and the basket following the customer, not the device; basket total equal to order total with the same coupon; expiry, the sweep and erasure |
| Integration | `WishlistTests` | idempotent add, newest first with live price and stock, archived hidden then shown again, removal; 404s, 401 for a guest, 403 for an admin account; merge skipping duplicates, unknown and foreign products; erasure |
| Integration | `TenantIsolationTests`, `AuthorizationBoundaryTests`, `ReviewModerationTests` | cross-store tokens and products; the public endpoint list; wishlist routes closed by the module flag |
| Architecture | `ModuleAndContractRuleTests`, `TenancyRuleTests` | allowed contracts; tenant ownership and composite keys |
| Frontend | `basketModel.test.js`, `wishlistModel.test.js`, `shippingOptions.test.js` | line mapping and blocking problems, quantity bounds, coupon message; merge ids, local toggle, fallback; the checkout shipping problem |

Not covered: two concurrent first writes by the same customer; `ReadyForCheckout` while a shipping choice is still pending; the behaviour of a basket whose products are priced in a currency the store no longer uses.

## Failure modes

| Situation | Code | HTTP | How it is handled |
|---|---|---|---|
| Product missing, unpublished or from another store (add) | `NotFound` | 404 | `AddBasketItemHandler` result, before any basket exists |
| Line not in the caller's basket (set quantity, remove) | `NotFound` | 404 | handler result |
| Quantity outside 1–99 (add) or 0–99 (set); malformed quote parameters | `ValidationFailed` | 400 | FluentValidation behaviour |
| Add or increase beyond what is available now | `InsufficientStock` | 422 | `BasketStock.ShortageAsync`, no write |
| A line's total above 99, or a 51st line | `InvalidBasketOperation` | 422 | `Basket` throws; `GlobalExceptionHandler` maps it |
| Coupon unusable, unknown, or module disabled | `InvalidCoupon`, `CouponNotFound`, `ModuleDisabled` inside `coupon` | 200 | reported in the quote; checkout turns it into 422 |
| Shipping choice missing or unusable | `ShippingMethodRequired`, `ShippingMethodUnavailable`, `ShippingNotAvailable` inside `shippingMethods` | 200 | reported in the quote; checkout turns it into 422 |
| Guest token expired, malformed, merged or foreign | — | 200 | treated as no basket; the cookie is deleted |
| Two first writes at once by the same customer | `DuplicateValue` | 409 | filtered unique index → `UniqueConstraintViolationException`; not covered by a test |
| Two first adds at once by a new guest | — | 200 | two baskets are created; the later cookie wins and the other expires unused ([ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md)) |
| Too many basket writes or quotes | `TooManyRequests` | 429 | rate limiter, with `Retry-After` |
| Wishlist: anonymous caller / staff or admin account | — / `CustomerAccountRequired` | 401 / 403 | `[Authorize]`, then `RequireCustomerId` |
| Wishlist already holds 200 products | `WishlistFull` | 422 | handler result |
| Two hearts clicked at once on the same product | `DuplicateValue` | 409 | unique index |
| Wishlist module disabled | `ModuleDisabled` | 404 | `TenantAvailabilityMiddleware`, before authentication |
| Store suspended or archived | `StoreUnavailable` | 503 | `TenantAvailabilityMiddleware` |

## Common change scenarios

See [ChangeGuide.md](ChangeGuide.md): add a pricing stage (tax under P-06); change basket expiry or the purge cadence; change basket limits; add a field to basket lines or the quote; change wishlist limits; replace the Catalog and Promotions leaks with contracts; wishlist alerts; variant selection in the basket.

## Known limitations

- **Tax is zero** until P-06 is decided.
- **`ReadyForCheckout` ignores shipping**, so a basket can read as ready while checkout would answer `ShippingMethodRequired`. The SPA compensates.
- **Category visibility is not checked.** The pipeline and the add handlers look only at product status, so a product in a disabled category is still sellable in the basket and at checkout; `WishlistQueries` on the other hand hides it, and such a hidden wishlist item still counts toward the 200 cap. [ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md) deferred this to Phase 9 and it has not been built.
- **One variant per product.** Basket commands take a product id and use the default variant; the domain already keys lines by variant, but the API, `PricingLine` and `Basket.LineFor` assume one.
- **Last write wins** across tabs, and an unavailable line stays visible until the shopper removes it (both deliberate).
- **The guest wishlist belongs to one browser**; there are no back-in-stock or price-drop alerts.
- **The pipeline always evaluates shipping**, even on a plain basket read, so a slow or remote rate provider would be called on every basket response.
- **Cross-module coupling:** the pipeline and the wishlist handlers use other modules' domain repositories (see *Dependencies*).
- **A GET can write** (`GET /api/basket` merges and deletes expired guest baskets).
- The purge runs on every API instance.

## Future evolution

- **DEFERRED:** the tax stage — open product decision **P-06** (inclusive or exclusive prices, per-store rate, invoice display), due "before the first sale" in [ProductRoadmap.md](../../12-ROADMAP/ProductRoadmap.md).
- Delivered in Phase 16 (storefront): the cart page and drawer, checkout and the wishlist page (see *Frontend* under Use cases).
- **DEFERRED to Phase 23:** a distributed lock for the store sweeps (the roadmap's Phase 6 entry defers it there; Phase 23's scope list does not yet name it).
- **FUTURE:** replacing the boundary leaks with contracts — a Catalog sellable-items contract (*ISellableItems* in [ModuleBoundaries.md](../../02-ARCHITECTURE/ModuleBoundaries.md)) and a Promotions coupon evaluator.
- **FUTURE:** shopper-chosen variants in the basket (waits for Catalog's option matrix); back-in-stock and price-drop alerts; a server-side guest wishlist, which [ADR-0033](../../11-ADR/0033-review-moderation-and-wishlist.md) rejected as more state than the feature is worth.
