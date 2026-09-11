# Shipping: change guide

> Read [README.md](README.md) first. This page lists the changes engineers actually make in this module and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive every change**

1. **Rate rules live in `ShippingMethod`.** The pricing pipeline asks the entity (`Serves`, `RateFor`) and never re-implements a rule.
2. **The free-shipping threshold is measured on the goods total after the discount.** Moving it before the discount lets a coupon buy free shipping.
3. **The destination country at checkout comes from the server** (the customer's address book), never from the request.
4. **A store with no active methods ships free with no choice.** Any change must keep that fallback, or stores that never configured shipping stop being able to sell.
5. **Problems are results, not exceptions:** `ShippingMethodRequired`, `ShippingMethodUnavailable`, `ShippingNotAvailable` appear in the quote and become 422 at checkout, before any write.
6. **The order keeps a snapshot, not a foreign key.** Nothing in this module may make an old order's shipping change.
7. **Tracking links are https templates containing `{number}`,** and the number is URL-encoded.
8. **Prices are always in the store currency;** no currency is ever accepted from a client.

**Files to read first:** `src/Souq.Domain/Entities/ShippingMethod.cs`, `src/Souq.Application/Features/Shipping/StoreShippingRates.cs`, `src/Souq.Application/Features/Shipping/Contracts/ShippingContracts.cs`, `src/Souq.Application/Features/Shipping/ShippingMethodUseCases.cs`, `src/Souq.Application/Features/Baskets/Pricing/PricingService.cs`, `src/Souq.Application/Features/Orders/Commands/CreateOrderHandler.cs`, `src/Souq.Domain/Entities/Order.cs`.

**Tests that guard the module:** `ShippingMethodTests`, `OrderShippingTests`, `StoreShippingRatesTests`, `ShippingMethodHandlersTests`, `PricingServiceTests`, `CreateOrderHandlerTests`, `ShippingTests`, `TenantIsolationTests`, `MigrationRehearsalTests`, and the frontend `shippingOptions.test.js`.

---

## I need a new rate strategy (weight, or zones)

- **Inspect:** `IShippingRateProvider` (it receives only a goods total and a country), `StoreShippingRates`, `ShippingMethod.RateFor` and `ShippingMethod.Serves`, `PricingService` stage 4, `TestShipping`, and `AllowedContracts` in `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`.
- **Rules to respect:** weight-based rates need weights, and products have none today — that is a Catalog change first, plus a contract to read them (add Catalog to `AllowedContracts["Shipping"]`; Catalog must not come to depend on Shipping). Zones are a grouping of destinations: the entity's countries list is the current, smaller version of that idea. Rules belong in Domain; the provider only orchestrates. Whatever the model, the threshold still measures goods after discount, and an unserved address still produces `ShippingNotAvailable` rather than a silent zero.
- **Steps:**
  1. Write an ADR: the rate model is a product-visible decision.
  2. Extend the contract to carry what the strategy needs — most likely the priced lines (product, quantity, weight) instead of just a total. Update `PricingService`, `TestShipping` and every substitute.
  3. Model zones or weight bands in Domain, with their own validation, and give the entity a method that prices a basket.
  4. Add the tables and admin endpoints for the new configuration; keep the existing flat method working, since stores rely on it.
  5. Keep `StoreShips` meaning "this store charges for delivery" so the checkout rule does not shift.
- **Tests:** `ShippingMethodTests` for the new rules; `StoreShippingRatesTests` for selection and ordering; `PricingServiceTests` for the pipeline; integration cases in `ShippingTests` for two destinations and a threshold; `MigrationRehearsalTests` for existing methods and orders.
- **API:** the quote's `options[]` keeps its shape if the price is still one number per option; the admin API gains endpoints.
- **Database:** new tables plus, if weights are stored on variants, a Catalog migration. Additive; a backfill is only needed if existing methods must become zone entries.
- **Security:** more admin surface behind `store.shipping.manage`; nothing shopper-supplied may influence the rate at checkout.
- **Docs and ADR:** a new ADR, this README, [Modules.md](../Modules.md), [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md).

## I need to integrate a carrier rates API

- **Inspect:** `IShippingRateProvider` and its registration in `src/Souq.Application/DependencyInjection.cs`, `StoreShippingRates`, `PricingService.ShippingAsync` (it calls the provider on **every** basket response, not only at checkout), `CreateOrderHandler` (it prices *before* opening the transaction), and how Payments keeps per-store provider credentials ([ADR-0031](../../11-ADR/0031-payments-and-refunds.md)).
- **Rules to respect:**
  - The adapter is Infrastructure, not Application: an external service lives behind the port and is implemented in `src/Souq.Infrastructure` (project rule 3), so the registration moves there.
  - **No network call inside a transaction** ([ADR-0021](../../11-ADR/0021-transaction-boundaries.md)). Checkout already quotes before `InTransactionAsync`; keep it that way.
  - A carrier outage must not break browsing. Today every basket read prices shipping, so an outage would make `GET /api/basket` fail; decide up front whether to call the carrier only when a country or method is present, to cache per store, country and goods band, or to fall back to the store's own methods.
  - Credentials are secrets: environment variables or user-secrets, per store and encrypted if each store has its own account. Never in committed `appsettings`.
- **Steps:**
  1. Write the adapter in Infrastructure against `IShippingRateProvider`, with a timeout, a retry policy and a circuit breaker of some form.
  2. Decide the composition: replace `StoreShippingRates`, or wrap it as a fallback.
  3. Move or override the DI registration; keep the in-process provider registered for tests and for stores without carrier credentials.
  4. Add an outcome code for "rates temporarily unavailable" and surface it like the existing shipping problems rather than as a 500.
  5. Reduce how often the provider is called from the pipeline if the call is remote.
- **Tests:** adapter tests against a fake HTTP handler (success, timeout, malformed response); `PricingServiceTests` with the substituted provider; an integration test proving a basket still renders when the carrier fails.
- **API:** possibly one new `errorCode` inside `shippingMethods`; otherwise unchanged.
- **Database:** credentials storage if rates are per store; no change to methods.
- **Security:** log no credentials and no full addresses; treat carrier responses as untrusted input, especially any URL.
- **Docs and ADR:** an ADR (a new external dependency), [Configuration.md](../../09-OPERATIONS/Configuration.md), this README's *External integrations*.

## I need to add a field to shipping methods

- **Inspect:** `ShippingMethod` (constructor, `Update`, the validation block), `ShippingMethodDto`, `ShippingMethodInput`, `ShippingMethodInputValidator`, `ShippingMethodMapping`, `ShippingMethodConfiguration`, `ShippingOption` (only if shoppers must see it), and `frontend/src/pages/admin/ShippingMethodFormDrawer.jsx`.
- **Rules to respect:** `Update` validates everything before assigning, so add the validation in the same place and keep that property. `PUT` replaces the whole method, so an older admin client that does not send the new field will clear it. If the field must appear on the order, it belongs in the snapshot (`Order.ApplyShipping`) — and then it is an Ordering migration too.
- **Steps:** add the property and its rule; extend the input, validator, mapping and DTO; migrate the column; extend the admin form; only then decide whether `ShippingOption` (and thus the shopper's quote and the order snapshot) needs it.
- **Tests:** `ShippingMethodTests` for the rule and for "a rejected update changes nothing"; `ShippingMethodHandlersTests` for the round trip; `ShippingTests` if it is visible to shoppers.
- **API:** additive on the admin endpoints; additive on the quote only if shoppers see it.
- **Database:** additive migration on `ShippingMethods` (plus `Orders` if it is snapshotted).
- **Docs and ADR:** this README's domain table; an ADR only if it changes pricing or eligibility.

## I need to change the free-shipping rule

- **Inspect:** `ShippingMethod.RateFor`, `ShippingMethod.FreeOver`, stage 4 of `PricingService` (it passes `subtotal − discount`), and `StoreShippingRatesTests`.
- **Rules to respect:** the "after discount" choice is recorded in [ADR-0032](../../11-ADR/0032-shipping-methods.md) and is deliberate; reversing it is a business decision, not a refactor. A per-country or per-method-group threshold changes eligibility, so it belongs in the entity, not in the provider. The threshold's currency must stay the price's currency.
- **Steps:** change the rule inside `ShippingMethod` (for example a threshold per country), or, to change *what* is measured, change what stage 4 passes and say so in the ADR; keep the rounding and currency guards.
- **Tests:** `ShippingMethodTests` (the threshold), `PricingServiceTests` (coupon plus threshold interaction — the case the rule exists for), `ShippingTests` (an order above and below the threshold).
- **API:** unchanged unless a new field appears.
- **Database:** a migration only for new configuration.
- **Docs and ADR:** amend ADR-0032; update this README and [BusinessRules.md](../../01-REQUIREMENTS/BusinessRules.md).

## I need a structured destination address on the order

- **Inspect:** `Order.ApplyShipping` and the order's single-line `ShippingAddress`, `PostalAddress` in `src/Souq.Domain/ValueObjects/PostalAddress.cs`, `CustomerAddress.ToPostalAddress`, `CreateOrderHandler` (it snapshots a single line plus the country), and `OrderConfiguration`.
- **Rules to respect:** this was deferred on purpose until a consumer exists — labels, or tax under P-06. The order snapshot is immutable after placement, and erasure must not strip it (it is part of the invoice), so adding fields adds retained personal data: check the erasure and retention rules in [ADR-0027](../../11-ADR/0027-customer-profile-and-erasure.md) before storing more.
- **Steps:** extend `Order` with the owned address snapshot; set it in `CreateOrderHandler` from the same `PostalAddress` the country already comes from; keep the single-line snapshot for existing views; migrate with nullable columns.
- **Tests:** `OrderShippingTests` (snapshot and immutability), `CreateOrderHandlerTests`, `MigrationRehearsalTests` (legacy orders keep their single line).
- **API:** order detail DTOs may expose the structured address to admins; shoppers already see their own.
- **Database:** additive, nullable columns on `Orders`.
- **Security:** more personal data at rest; confirm the retention plan and never log it.
- **Docs and ADR:** Ordering's module doc, this README, and an ADR if it is the enabler for tax or labels.

## I need to create shipments or print labels with a carrier (FUTURE)

- **Inspect:** `Order.MarkAsShipped` and `UpdateOrderStatusHandler` (where staff enter a tracking number today), `ShippingMethod.Carrier` and the template, `frontend/src/pages/admin/ShipOrderDrawer.jsx`.
- **Rules to respect:** creating a shipment is a network call, so it stays outside any transaction, and the order's status change must not depend on the carrier being up — record the failure and let staff retry, the way a failed refund is handled. The tracking number and label URL are new data on the order (Ordering), not on the method. A label request needs the structured address above.
- **Steps:** add a shipment port in this module (separate from the rate provider), implement it in Infrastructure, call it from the Ordering use case that ships an order, and store the returned tracking number through the existing `MarkAsShipped` path.
- **Tests:** adapter tests with a fake carrier; an application test that a carrier failure leaves the order shippable and reports the problem; an integration test for the happy path.
- **API:** admin endpoints gain a "create shipment" action; the customer-facing tracking link already exists.
- **Database:** new columns or a shipment table in Ordering.
- **Security:** carrier credentials as secrets, per store; no addresses in logs.
- **Docs and ADR:** a new ADR; this README's *External integrations*; [Modules.md](../Modules.md).
