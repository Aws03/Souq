# Shipping module

> **Code:** `src/Souq.Application/Features/Shipping`, `src/Souq.Domain/Entities/ShippingMethod.cs`, `src/Souq.API/Controllers/ShippingMethodsController.cs` · **Decisions:** [ADR-0032](../../11-ADR/0032-shipping-methods.md), [ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md) (the pipeline stage it fills), [ADR-0029](../../11-ADR/0029-orders-lifecycle.md) (placement freezes totals) · **Change guide:** [ChangeGuide.md](ChangeGuide.md)

## Purpose

Shipping owns how an order reaches the customer and what that costs: the methods a store offers, the rules deciding whether a method serves an address and at what price, and the carrier link a customer uses to track a parcel. It is a module of its own because delivery pricing is a genuine variation point — today a table the store fills in, tomorrow a carrier's API — and the rest of the system should not notice the difference.

## Responsibilities

- The store's shipping methods and their admin lifecycle (list, create, update, delete).
- The rules inside `ShippingMethod`: price, free-shipping threshold, countries served, delivery estimate, carrier and tracking-link template, active flag and display order.
- The rate strategy `IShippingRateProvider`, implemented today by `StoreShippingRates` over the store's own methods.
- Building a carrier tracking link from a template and a tracking number (`ShippingMethod.TrackingUrl`).

## Not this module's job

| Concern | Owner |
|---|---|
| Deciding which method a basket uses, and pricing it into a total | Shopping — pipeline stage 4 turns this module's quote into options and problems |
| The destination address and its country | Customers (the address book); Ordering reads it at checkout |
| The order's shipping snapshot, the tracking number, the shipped and delivered transitions | Ordering — `Order.ApplyShipping`, `Order.MarkAsShipped` |
| Telling the customer a parcel shipped | Notifications |
| Tax on shipping | Nobody yet — open product decision P-06 |

## Business concepts

- **Shipping method:** a delivery option a store offers, with a flat price in the store currency.
- **Free-shipping threshold:** the goods total at or above which the method costs nothing.
- **Countries served:** ISO codes; a method with none serves everywhere.
- **Delivery estimate:** a minimum and maximum number of days, informational.
- **Carrier and tracking template:** the company delivering, and an https link pattern containing `{number}`.
- **Store ships:** the store has at least one active method, which makes choosing one mandatory.
- **Shipping snapshot:** what the order keeps of the chosen method, so later edits never rewrite history.

## Domain model

| Type | Kind | Path | Invariants it guards |
|---|---|---|---|
| `ShippingMethod` | aggregate root | `src/Souq.Domain/Entities/ShippingMethod.cs` | Name required and at most `ShippingMethod.NameMaxLength` (100), trimmed; price is `Money`, so never negative; a free-shipping threshold must be above zero and expressible in the price's currency; `MinDays` and `MaxDays` are given together, between 0 and `ShippingMethod.MaxEstimateDays` (90), minimum not above maximum; carrier at most `ShippingMethod.CarrierMaxLength` (100); the tracking template must be an absolute **https** URL containing `ShippingMethod.TrackingNumberToken` (`{number}`) and at most `ShippingMethod.TrackingUrlMaxLength` (300); countries are unique upper-case two-letter codes, sorted, within `ShippingMethod.CountriesMaxLength` (300); `Update` validates everything **before** assigning anything, so a rejected update changes nothing; `RateFor` refuses a basket in another currency |
| `InvalidShippingMethodException` | domain exception | `src/Souq.Domain/Exceptions/InvalidShippingMethodException.cs` | Stable code `InvalidShippingMethod` |
| `Money` | value object (shared kernel) | `src/Souq.Domain/ValueObjects/Money.cs` | Price and threshold amounts |

Notes:

- **Aggregate boundary:** a method is a small standalone root with no children. The countries list is a normalised comma-separated string, read back through `ShippingMethod.CountryList`.
- **No concurrency token.** Methods are edited by admins only, so the last write wins. Nothing else points at a method by key.
- **No state machine,** just `IsActive`: deactivating pauses a method, deleting removes it for good — safe because orders carry a snapshot rather than a foreign key.
- `ShippingMethod.TrackingUrl` is a static helper that Ordering's `Order` calls; the rule that a tracking link is https and templated therefore lives here even though the data lives on the order.

## How it works

### Rates

`StoreShippingRates` implements `IShippingRateProvider.QuoteAsync(goods, country)`:

1. Read the store's active methods (`IShippingMethodRepository.ListAsync(activeOnly: true)`, tenant-filtered).
2. Skip any method whose price currency differs from the basket's — if a store changed currency, showing an old-currency method would price the basket wrongly.
3. Keep the methods that serve the destination: `ShippingMethod.Serves(country)` is true when the method lists no countries, or when the address has a known two-letter code in its list. An address with no country matches only unrestricted methods.
4. Price each one with `ShippingMethod.RateFor(goods)`: free when `goods` reaches the threshold, otherwise the flat price.
5. Order by the admin's `SortOrder`, then by cost, then by id.
6. Report `ShippingQuote.StoreShips` — whether the store has **any** active method, even one that does not serve this address.

The threshold is measured on the goods total **after** the coupon discount, because measuring it before would let a coupon pull an order under the threshold and still ship free ([ADR-0032](../../11-ADR/0032-shipping-methods.md)).

Shopping's pipeline turns the quote into shopper-visible results: the chosen option's cost enters the total, and problems become `ShippingMethodUnavailable` (the chosen method does not serve this address), `ShippingNotAvailable` (nothing serves it) or `ShippingMethodRequired` (the store ships but nothing was chosen). A store with no active methods ships free with no choice, so stores that never configure shipping keep working exactly as before Phase 12.

### Where the destination country comes from

- **At checkout** the server takes it from the customer's own address book: `CreateOrderHandler` resolves `ShippingAddressId` in the caller's book and reads `CustomerAddress.ToPostalAddress().Country`. The client never sends a country for an order, so it cannot claim a cheaper zone.
- **In the quote** the country is a query parameter, because the quote is only a preview and checkout checks again. `GetBasketValidator` requires two letters and the handler upper-cases it. The SPA fills it from the selected saved address (`countryOfChoice` in `frontend/src/features/checkout/shippingOptions.js`).
- **A typed, free-text address has no country**, so only methods without country limits apply; `AddressStep` tells the shopper to pick a saved address to see the others.

### The order snapshot

`CreateOrderHandler` calls `Order.ApplyShipping(name, cost, carrier, trackingUrlTemplate, minDays, maxDays, country)` before `Order.Place`, which writes `ShippingMethodName`, `ShippingAmount`, `ShippingCarrier`, `ShippingTrackingUrlTemplate`, `ShippingMinDays`, `ShippingMaxDays` and `ShippingCountry` on the order (Ordering's table, `OrderConfiguration`). There is deliberately **no foreign key** to `ShippingMethods`: editing or deleting a method never rewrites an old order, which is exactly why deletion can be a hard delete.

`Order.TotalAmount` is subtotal − discount + shipping, frozen at placement; the payment intent charges that total and refunds return it. The method's carrier becomes the shipment's default carrier, and staff may name another when they ship (`Order.MarkAsShipped`). `Order.TrackingUrl` composes `ShippingMethod.TrackingUrl(template, trackingNumber)`, URL-encoding the number, and it appears in the order detail and on the public tracking page (`frontend/src/pages/OrderTracking.jsx`, `frontend/src/pages/OrderDetail.jsx`).

### No module flag

Shipping is not optional: `StoreModules.All` is `promotions`, `reviews` and `wishlist`. A store that does not want to charge for delivery simply has no active methods.

## Use cases

| Use case | Command or query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| List methods, active and inactive | `ListShippingMethodsQuery` | `ListShippingMethodsHandler` | `store.shipping.manage` | `GET /api/admin/shipping-methods` |
| Create a method | `CreateShippingMethodCommand` | `CreateShippingMethodHandler` | `store.shipping.manage` | `POST /api/admin/shipping-methods` |
| Update a method (full replacement, including the active flag) | `UpdateShippingMethodCommand` | `UpdateShippingMethodHandler` | `store.shipping.manage` | `PUT /api/admin/shipping-methods/{id}` |
| Delete a method | `DeleteShippingMethodCommand` | `DeleteShippingMethodHandler` | `store.shipping.manage` | `DELETE /api/admin/shipping-methods/{id}` |
| Price the options for a basket | `IShippingRateProvider.QuoteAsync` (not a MediatR request) | `StoreShippingRates` | Shopping's pricing pipeline | — (surfaces in `GET /api/basket/quote`) |

`ShippingMethodInput` carries the whole method; `ShippingMethodInputValidator` checks shape (name, price ≥ 0, threshold > 0, lengths, days within 0–90, at most 60 countries, sort order 0–10 000) and the entity checks meaning. The price currency is always the store's — no currency is accepted from the client. `ShippingMethodMapping` maps the input onto the entity and applies the active flag.

## Public contracts

| Contract | Path | Implementation | Callers |
|---|---|---|---|
| `IShippingRateProvider`, with `ShippingOption` and `ShippingQuote` | `src/Souq.Application/Features/Shipping/Contracts/ShippingContracts.cs` | `StoreShippingRates`, registered in `src/Souq.Application/DependencyInjection.cs` | `PricingService` (Shopping) |

`ShippingOption` also reaches Ordering, through `PriceQuote.ShippingOutcome`: `CreateOrderHandler` reads the selected option to build the order snapshot. That is the allowed Ordering → Shipping contract reference.

A carrier integration replaces the single registration of `IShippingRateProvider` and nothing else — that is the point of the port ([ADR-0032](../../11-ADR/0032-shipping-methods.md)).

## Dependencies

- **Uses:** its own `IShippingMethodRepository`, plus the shared kernel (`Money`, `ITenantContext` for the store currency, `IUnitOfWork`, `Result` / `Error`). It calls no other module's contracts.
- **Used by:** Shopping (`IShippingRateProvider`) and Ordering (`ShippingOption` through the quote).
- **Boundary leaks:** Ordering's domain references this module's domain — `Order.ShippingMethodMaxLength` is `ShippingMethod.NameMaxLength`, `Order.TrackingUrl` calls `ShippingMethod.TrackingUrl`, and `OrderConfiguration` uses `ShippingMethod.TrackingUrlMaxLength`. The architecture tests do not inspect the Domain layer, so this passes; it is coupling in the direction Ordering → Shipping, which the contract map already allows at the Application level.
- **Enforced vs convention:** `ModuleAndContractRuleTests` allows Shopping → Shipping and Ordering → Shipping contracts and would fail any reference from Shipping into another feature folder. `TenancyRuleTests` enforces tenant ownership and the tenant-carrying foreign keys. [Modules.md](../Modules.md) lists Shipping → Platform: in practice that is `ITenantContext`, a shared building block, not a module contract.

## Data ownership

| Table | EF configuration | Tenant-owned | Concurrency | Constraints and indexes that encode rules |
|---|---|---|---|---|
| `ShippingMethods` | `ShippingMethodConfiguration` | yes | none | `Price` and its currency as owned columns; `FreeOverAmount` with money precision; `Countries` a required non-Unicode string of at most 300 characters; index `(TenantId, IsActive, SortOrder)`, which is exactly the provider's read pattern; no foreign key from `Orders` |

- Migration `Phase12Shipping` is additive: the new table, plus the snapshot columns on `Orders` (`ShippingAmount` defaults to 0, the rest stay null). `MigrationRehearsalTests` asserts that legacy orders keep zero shipping and unchanged totals.
- The snapshot columns on `Orders` belong to Ordering; this module supplies their values through the quote.
- This module reads no other module's data.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/admin/shipping-methods` | `store.shipping.manage` | — | list (including inactive) |
| POST | `/api/admin/shipping-methods` | `store.shipping.manage` | — | create, 201 with the id |
| PUT | `/api/admin/shipping-methods/{id}` | `store.shipping.manage` | — | update, 204 |
| DELETE | `/api/admin/shipping-methods/{id}` | `store.shipping.manage` | — | delete, 204 |

Customers never call these routes: they see the options for their address inside `GET /api/basket/quote` (Shopping), which exposes `methodId`, `name`, `cost`, `carrier`, `minDays` and `maxDays` — not the tracking template.

Frontend: `getShippingMethods`, `createShippingMethod`, `updateShippingMethod`, `deleteShippingMethod` in `frontend/src/api/client.js`; screens in `frontend/src/pages/admin/ShippingMethods.jsx` and `frontend/src/pages/admin/ShippingMethodFormDrawer.jsx`; the shopper's choice logic in `frontend/src/features/checkout/shippingOptions.js` (`countryOfChoice`, `pickShippingMethod`, `estimateLabel`, `shippingProblem`) and `frontend/src/features/checkout/shippingChoice.js`; staff enter a tracking number in `frontend/src/pages/admin/ShipOrderDrawer.jsx`.

## Security and permissions

- `store.shipping.manage` is granted to the store administrator role only; store staff do not have it, and a customer calling an admin route gets 403 (`ShippingTests`).
- The tracking template is validated as absolute **https** containing `{number}`, so no `javascript:` or plain-http link can be pushed to customers, and the tracking number is URL-encoded when the link is built.
- Rate rules never depend on client-supplied data at checkout: the country comes from the server-side address book.
- Method changes are not audited (no request in this module implements `IAuditable`).

## Tenant behaviour

- `ShippingMethod` is `ITenantOwned`: the query filter scopes every read and the write guard stamps the store, so another store's method id is a 404 on update and delete (`TenantIsolationTests`).
- Prices are created and updated in the store's currency, taken from `ITenantContext`; a method left over from a previous currency is skipped by the provider rather than shown.
- The provider reads only the host store's methods, so one store's rates can never price another store's basket.

## Events and background work

- `ShippingMethod` raises no domain events; this module has no hosted service and writes nothing to the outbox.
- Shipment-related messages come from Ordering's status changes; the customer email templates read the carrier from the order's snapshot (`src/Souq.Application/Features/Notifications/OrderAndStockHandlers.cs`).

## External integrations

None today. A carrier API is the reason `IShippingRateProvider` exists; see *Future evolution* and [ChangeGuide.md](ChangeGuide.md).

## Tests

| Layer | Class | What it covers |
|---|---|---|
| Domain | `ShippingMethodTests` | flat price and the free threshold; country normalisation, "everywhere" and an unknown country; the tracking link built and encoded; each creation rule; the threshold in the price's currency; a rejected update changing nothing |
| Domain | `OrderShippingTests` | an order without shipping keeps its old total; shipping enters the total and is frozen at placement; the method's carrier survives unless staff override it; snapshot currency, name and two-letter country guards |
| Domain | `DomainExceptionCodeTests` | the `InvalidShippingMethod` code |
| Application | `StoreShippingRatesTests` | the country filter, ordering by sort order then price, the threshold, methods in an old currency skipped, and `StoreShips` |
| Application | `ShippingMethodHandlersTests` | creation in the store currency including "created inactive"; update applying every field and the active flag; 404 for another store's method on update and delete |
| Application | `PricingServiceTests` | the chosen method priced after the discount and entering the total; a required choice; a store without methods; a store whose methods do not serve the address |
| Application | `CreateOrderHandlerTests` | a method is required, and the chosen one flows into the total, the payment intent and the snapshot |
| Application | `TestShipping` (test double) | the substitute rate provider other tests build on |
| Integration | `ShippingTests` | options per country, checkout requiring a choice, shipping in the total and the tracking link; the free threshold and a store without methods ordering with no choice; admin update and delete, rule errors by code, and a customer refused |
| Integration | `TenantIsolationTests`, `MigrationRehearsalTests` | cross-store method ids; legacy orders keeping zero shipping |
| Frontend | `shippingOptions.test.js`, `shippingChoice.test.js` | the address's country, keeping or replacing the chosen method, the estimate wording, when the order is blocked; address choice and payload |

Not covered: a store whose only active methods are in a currency it no longer uses (see *Known limitations*).

## Failure modes

| Situation | Code | HTTP | How it is handled |
|---|---|---|---|
| Name, price, threshold, lengths, days, country count or sort order rejected by the validator | `ValidationFailed` | 400 | `ShippingMethodInputValidator` |
| Tracking template not https or missing `{number}`; country codes not two letters; only one of min/max days, or min above max; basket currency not the method's | `InvalidShippingMethod` | 422 | `ShippingMethod` throws |
| Price or threshold with more decimals than the currency allows | `InvalidMoney` | 422 | `Money` throws |
| Method missing, or belonging to another store | `NotFound` | 404 | handler result |
| Customer or staff without the permission calls an admin route | — | 403 | `[HasPermission(Permissions.Store.Shipping)]` |
| Store ships but nothing was chosen | `ShippingMethodRequired` | 200 in the quote / 422 at checkout | `PricingService` |
| Chosen method does not serve the address | `ShippingMethodUnavailable` | 200 / 422 | `PricingService` |
| No method serves the address | `ShippingNotAvailable` | 200 / 422 | `PricingService` |
| Snapshot rules broken (empty name, wrong currency, malformed country, order already placed) | `InvalidOrderOperation` | 422 | `Order.ApplyShipping`; defensive |
| Store changed currency and no active method matches it | `ShippingNotAvailable` | 422 at checkout | the provider skips them while `StoreShips` still counts them |

## Common change scenarios

See [ChangeGuide.md](ChangeGuide.md): add a rate strategy (weight or zone); integrate a carrier rates API; add a field to shipping methods; change the free-shipping rule; snapshot a structured address; create labels and shipments with a carrier.

## Known limitations

- **Flat rates only:** no weight-based or zone tables, because products have no weights ([ADR-0032](../../11-ADR/0032-shipping-methods.md)).
- **No carrier integration:** no live rates, no labels, no pickup booking.
- **Estimates are informational:** no business-day calendars and no cut-off times.
- **One name per method,** in the store's language; per-language names were left to the white-label work and not built.
- **A typed address sees only unrestricted methods** — deliberate, and explained in the checkout UI.
- **The order keeps only the destination country**, not a structured address; labels or tax would need more.
- **`ShippingQuote.StoreShips` counts active methods in any currency**, so a store that changed currency and kept only old-currency methods blocks checkout with `ShippingNotAvailable` instead of falling back to free shipping.
- **No audit trail** for method changes, and no concurrency token, so two admins editing at once silently overwrite each other.
- **Tax on shipping does not exist** (P-06).

## Future evolution

- **FUTURE:** weight- or zone-based rates; the roadmap lists "shipping zones and carrier APIs" as work that can follow the first release without architectural change.
- **FUTURE:** a carrier rate provider behind `IShippingRateProvider`, and later label printing and shipment creation.
- **DEFERRED:** a structured address snapshot on the order — deliberately postponed until a consumer needs it (labels, or tax under P-06).
- **FUTURE:** per-language method names.
- **PLANNED:** Phase 16 (storefront) rebuilds the checkout screens that present these options.
