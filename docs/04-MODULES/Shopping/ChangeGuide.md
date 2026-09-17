# Shopping: change guide

> Read [README.md](README.md) first. This page lists the changes engineers actually make in this module and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive every change**

1. **One pipeline.** Every amount a shopper sees or pays comes from `IPricing`. Never compute a total in a handler, a controller or the browser: `CartContext` replaces its state with the server's basket precisely so they cannot drift.
2. **Baskets reserve nothing.** Only checkout reserves stock. Availability reads are advisory messages, never guarantees.
3. **The basket stores no price.** `BasketLine` is variant plus quantity; prices come from the live catalog at every quote.
4. **The guest token is a secret.** Only its SHA-256 hash may be stored or logged, and the cookie stays `HttpOnly`, `SameSite=Strict` and scoped to `/api/basket`.
5. **Coupon and shipping problems are results, not failures,** in the quote — and `422` at checkout, before any write.
6. **Every request touches only the caller's own basket or wishlist.** No owner id in any route or command.
7. **Everything is tenant-owned.** New tables implement `ITenantOwned` and use composite foreign keys `(TenantId, Id)`.

**Files to read first:** `src/Souq.Application/Features/Baskets/Pricing/PricingService.cs`, `src/Souq.Application/Features/Baskets/Contracts/PricingContracts.cs`, `src/Souq.Application/Features/Baskets/BasketResolver.cs`, `src/Souq.Application/Features/Baskets/BasketUseCases.cs`, `src/Souq.Application/Features/Baskets/BasketViews.cs`, `src/Souq.Domain/Entities/Basket.cs`, `src/Souq.Application/Features/Wishlist/WishlistUseCases.cs`, `src/Souq.API/Controllers/BasketController.cs`, `src/Souq.Application/Features/Orders/Commands/CreateOrderHandler.cs`.

**Tests that guard the module:** `BasketTests` (Domain and Integration), `WishlistItemTests`, `PricingServiceTests`, `BasketHandlersTests`, `BasketCheckoutTests`, `WishlistHandlersTests`, `CreateOrderHandlerTests`, `WishlistTests`, `TenantIsolationTests`, `AuthorizationBoundaryTests`, `ModuleAndContractRuleTests`, and the frontend `basketModel.test.js`, `wishlistModel.test.js`, `shippingOptions.test.js`.

---

## I need to add a pricing stage (for example tax, under P-06)

- **Inspect:** stage 5 in `PricingService.QuoteAsync`; `PriceQuote` and `BasketDto` (both already carry `Tax`); `BasketViews.BuildAsync`; `CreateOrderHandler`; `Order.TotalAmount` and `Order.Place` in `src/Souq.Domain/Entities/Order.cs`; `OrderCreatedDto`; `frontend/src/features/basket/basketModel.js` (`EMPTY_BASKET`) and `frontend/src/pages/checkout/OrderSummaryPanel.jsx`.
- **Rules to respect:** P-06 is a product decision (prices tax-inclusive or exclusive, who sets the rate, whether invoices must show tax) — take the decision before writing code. The rule itself belongs in Domain, not in the pipeline. Round once, through `Money.FromCalculation` ([ADR-0014](../../11-ADR/0014-money-precision.md)). The order must freeze whatever it charges: the payment intent uses `Order.TotalAmount`. Keep the stage order fixed, so the contract shape does not move.
- **Steps:**
  1. Record the decision in [ProductRoadmap.md](../../12-ROADMAP/ProductRoadmap.md) (P-06) and write an ADR; it changes what the store charges.
  2. Decide where the rate lives (a store setting owned by Platform, or a new module) and expose it to the pipeline behind a contract in the owning module's `Contracts` folder; add that module to `AllowedContracts["Shopping"]` in `ModuleAndContractRuleTests` and check the graph stays acyclic.
  3. Replace the explicit zero in stage 5 with the computed tax, keeping `total = goods + shipping + tax`. For tax-inclusive prices, compute the tax portion without changing the total.
  4. Ordering: add the snapshot to `Order` (a column, included in `TotalAmount`, frozen at `Place`), set it in `CreateOrderHandler` from `quote.Tax`, and surface it in the order DTOs.
  5. Frontend: show the tax line in the basket drawer and the checkout summary.
- **Tests:** `PricingServiceTests` for stage values and rounding; `CreateOrderHandlerTests` for order total, payment-intent amount and snapshot; a Domain test alongside `OrderShippingTests` for the frozen total; extend the basket-equals-order integration test in `BasketTests`; `MigrationRehearsalTests` for existing orders (tax 0, totals unchanged).
- **API:** `tax` already exists in `BasketDto`, so the basket contract does not change; order DTOs gain a field (additive). No new error codes unless a store can be misconfigured — if so, report it as an outcome in the quote, not an exception.
- **Database:** additive migration on `Orders` (default 0 for existing rows) and wherever the rate is stored. Reversible.
- **Security:** the rate is per store and must respect the tenant filter; nothing sensitive is logged.
- **Docs and ADR:** this README, the Ordering module doc, a new ADR, the P-06 row in the roadmap, and [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md).

## I need to change basket expiry or the purge cadence

- **Inspect:** `BasketSettings`, the `Basket` section in `src/Souq.API/appsettings.json`, the validation in `src/Souq.Infrastructure/DependencyInjection.cs`, `BasketResolver.ExpiryFor`, `ResolvedBasket.Written` (the cookie follows the basket's expiry), `BasketCheckout.ConsumeAsync`, `BasketCleanupService`, `PurgeExpiredBasketsCommand`.
- **Rules to respect:** expiry slides on change only — making reads extend it would turn `GET /api/basket` into a write on every page view. A guest's expired basket is deleted, a customer's is emptied and its expiry reset. The purge stays a bounded batch so its transaction stays small.
- **Steps:** for new values, change configuration per environment (`Basket:GuestLifetimeDays`, `Basket:CustomerLifetimeDays`, `Basket:CleanupIntervalMinutes`) — no code change. For values outside the accepted ranges, widen the `.Validate` calls in `AddBaskets`. For a different policy (an absolute expiry, or reads extending the lifetime), change `Basket` and `BasketResolver`, since the entity owns `ExpiresAt`.
- **Tests:** `BasketTests` (Domain) for the entity rule; `BasketHandlersTests` for the resolver and the purge; the expiry test in the integration `BasketTests`.
- **API:** unchanged, though the cookie's `Expires` moves with the basket.
- **Database:** none. `(TenantId, ExpiresAt)` already serves the purge.
- **Security:** a shorter guest lifetime means fewer anonymous rows retained.
- **Docs and ADR:** this README and [Configuration.md](../../09-OPERATIONS/Configuration.md). A policy change (not just values) amends [ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md).

## I need to change the basket limits (lines, or quantity per line)

- **Inspect:** `Basket.MaxLines`, `Basket.MaxQuantityPerLine`, `Basket.EnsureQuantity` and `Basket.MergeFrom`, `CK_BasketLines_Quantity` in `BasketLineConfiguration`, `AddBasketItemValidator`, `SetBasketItemQuantityValidator`, `CreateOrderValidator` (it caps request items with `Basket.MaxLines`), and `MAX_QUANTITY` in `frontend/src/features/basket/basketModel.js`.
- **Rules to respect:** an explicit request over the limit is rejected, never trimmed; only the merge trims silently, because it is automatic and must not fail a sign-in.
- **Steps:** change the constant; regenerate the check constraint in a migration (the constraint text is built from `Basket.MaxQuantityPerLine`); update the frontend constant; re-read `CreateOrderValidator`, whose line cap follows `Basket.MaxLines`.
- **Tests:** the limit tests in Domain `BasketTests`; the validators; the add/set paths in `BasketHandlersTests`.
- **API:** validation messages change; `InvalidBasketOperation` messages mention the new numbers.
- **Database:** a migration that drops and recreates `CK_BasketLines_Quantity`. Lowering a limit needs existing rows fixed **before** the constraint is added, or the migration fails on real data.
- **Security:** larger baskets mean heavier quotes (one catalog query, but more rows); the `basket` rate limit stays the guard.
- **Docs and ADR:** this README and [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md); no ADR for a number change.

## I need to add a field to basket lines or to the quote

- **Inspect:** `PricedLine` and `PriceQuote` (contract types shared with Ordering), `PricingService.Price`, `BasketLineDto` / `BasketDto`, `BasketViews.BuildAsync`, `toCartItems` in `frontend/src/features/basket/basketModel.js`.
- **Rules to respect:** never store the value on `BasketLine`; derive it in the pipeline from live data, so the basket cannot show something the order will not honour. `PricedLine` is consumed by `CreateOrderHandler`, so keep changes additive and check what the order snapshot should do with the new value.
- **Steps:** extend `PricedLine` (and `BasketLineDto`) with an optional member; fill it in `PricingService.Price` and map it in `BasketViews`; render it in the frontend model and components.
- **Tests:** `PricingServiceTests` for the new value, `basketModel.test.js` for the mapping, and the integration `BasketTests` body if the field must appear over HTTP.
- **API:** additive JSON field; no version change.
- **Database:** none.
- **Security:** do not expose anything the storefront should not see (for example internal cost or supplier data).
- **Docs and ADR:** this README's pipeline table and [ApiDocumentation.md](../../05-API/ApiDocumentation.md).

## I need to change the wishlist limits

- **Inspect:** `WishlistItem.MaxItemsPerCustomer`, `WishlistItem.HasRoom`, `AddToWishlistHandler` (the `WishlistFull` result), `MergeWishlistHandler` (the room calculation), `MergeWishlistValidator`, `WishlistQueries` (the list is returned whole, with no paging, precisely because it is capped), and `MAX_WISHLIST` in `frontend/src/features/wishlist/wishlistModel.js`.
- **Rules to respect:** the cap is what makes an unpaged list safe. Raising it much means adding paging to `IWishlistQueries`, the endpoint and the page; the merge must never exceed the cap, and a merge that hits it skips silently rather than failing.
- **Steps:** change the constant; align the merge validator's maximum and the frontend constant; if the new cap is large, add paging first.
- **Tests:** `WishlistItemTests` (the cap), `WishlistHandlersTests` (full list refused, merge capped), `wishlistModel.test.js` (the newest-N rule).
- **API:** the `WishlistFull` message and the merge validation message change; the response shape does not.
- **Database:** none.
- **Security:** a bigger cap means a bigger unpaged response per request; consider it a denial-of-service surface.
- **Docs and ADR:** this README and [ADR-0033](../../11-ADR/0033-review-moderation-and-wishlist.md) (which records 200).

## I need to remove the boundary leaks (pipeline and wishlist reading other modules' repositories)

- **Inspect:** `PricingService`, `AddBasketItemHandler`, `AddToWishlistHandler`, `MergeWishlistHandler`, `WishlistQueries`, and `AllowedContracts` in `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`.
- **Rules to respect:** a contract lives in the owning module's `Contracts` folder and exposes DTOs, never entities; no cycles (Promotions must not end up calling Shopping); behaviour must not change — the same error codes and messages.
- **Steps:**
  1. Catalog: add a sellable-items contract ([ModuleBoundaries.md](../../02-ARCHITECTURE/ModuleBoundaries.md) names *ISellableItems*) returning name, translations, image, default variant id, unit price and a sellable flag. Decide there whether category visibility counts as sellable, which also closes the gap listed under *Known limitations*.
  2. Promotions: add an evaluation contract returning the outcome and discount for a code, subtotal and customer, keeping every rule inside `Coupon`.
  3. Switch `PricingService` and the three handlers to the contracts, then add `Catalog` and `Promotions` to `AllowedContracts["Shopping"]`.
  4. Leave `WishlistQueries` for last: it is an Infrastructure projection, and the read side is allowed to join tables ([ADR-0008](../../11-ADR/0008-cqrs-strategy.md)); document it rather than rewriting it, unless Catalog gains a suitable read port.
- **Tests:** `PricingServiceTests` and `WishlistHandlersTests` move to the new substitutes; `ModuleAndContractRuleTests` gets the new map; every integration test must pass unchanged — that is the proof behaviour did not move.
- **API:** none. **Database:** none.
- **Security:** the new contracts must stay tenant-filtered; do not add an id-based lookup that bypasses the filter.
- **Docs and ADR:** [Modules.md](../Modules.md), this README's *Dependencies*, and [TechnicalDebt.md](../../12-ROADMAP/TechnicalDebt.md). No ADR: this implements the boundary that [ADR-0004](../../11-ADR/0004-module-boundaries.md) already describes.

## I need back-in-stock or price-drop alerts for wishlist items (FUTURE)

- **Inspect:** `WishlistItem` (it stores no price, deliberately), `IWishlistQueries`, the outbox and handlers in `src/Souq.Application/Features/Notifications`, and how Inventory raises its stock event ([ADR-0034](../../11-ADR/0034-notifications-outbox.md)).
- **Rules to respect:** a price-drop alert needs a baseline the wishlist does not have today — adding one makes the wishlist hold data that erasure must delete. No request path may wait on the email provider (an architecture test confines `IEmailSender` to Notifications). Notifications reads state through ports; it must not call Shopping use cases synchronously.
- **Steps:** raise the trigger as a domain event in the module that owns the fact (Catalog for price, Inventory for stock); handle it in Notifications; read affected wishlists through a new Shopping read contract; enqueue outbox messages; add opt-out handling before sending anything.
- **Tests:** handler tests with a substituted read port; an integration test that a change produces exactly one message per customer and none after erasure.
- **API:** possibly a preference endpoint; the wishlist response may gain a "notify me" flag.
- **Database:** new columns or a table; include them in `CustomerErasure`.
- **Security:** consent and opt-out are part of the feature; never log addresses or tokens.
- **Docs and ADR:** a new ADR (a new outbound message type and new personal data).

## I need shoppers to choose a variant in the basket (FUTURE)

- **Inspect:** `AddBasketItemCommand` (product id only) and `AddBasketItemHandler` (it takes `product.DefaultVariant.Id`), `Basket.Add` and the unique `(BasketId, VariantId)` index (already variant-keyed), `Basket.LineFor` and the set/remove handlers (product-keyed), `PricingLine` (product id only), `PricingService.Price`, `IBasketCheckout.ConsumeAsync` (groups by product), and `CreateOrderHandler`'s reservation lines.
- **Rules to respect:** the domain already supports several variants per basket; the API, the pricing contract and the product-keyed lookups do not. Catalog's option matrix is deferred, so this change starts there (D-21: the default variant is the sellable unit today).
- **Steps:** add the variant to Catalog first; then to the commands and routes, `PricingLine`, `PricedLine`, the order item snapshot, and the product-keyed lookups; keep the old routes working by falling back to the default variant during the transition.
- **Tests:** Domain `BasketTests` (two variants of one product), `PricingServiceTests`, `BasketCheckoutTests` (consumption per variant), `CreateOrderHandlerTests`.
- **API:** breaking for `PUT` and `DELETE /api/basket/items/{productId}` unless the old shape is kept.
- **Database:** none for baskets; order items may need a variant column.
- **Docs and ADR:** an ADR, since it changes what "a line" means across Shopping and Ordering.
