# ADR-0032: Shipping: store-defined methods behind a rate-provider strategy, chosen at checkout, snapshotted on the order

- **Status:** Accepted (implemented in Phase 12), 2026-09-11.
- **Builds on:**
  - [ADR-0014](0014-money-precision.md): money.
  - [ADR-0027](0027-customer-profile-and-erasure.md): the address book, whose `PostalAddress` carries an ISO country.
  - [ADR-0028](0028-basket-and-pricing-pipeline.md): the pricing pipeline, whose shipping stage was an explicit zero.
  - [ADR-0029](0029-orders-lifecycle.md): placement freezes the totals.

## Context

Before Phase 12:
- **Pricing:** the pipeline's shipping stage was an explicit zero, and the UI said "Free".
- **Orders** had only a tracking number and a carrier, typed by staff at shipping.
- **Totals** didn't include shipping.
- **Deferred to this phase:** Phase 9 left the shipping-method snapshot and structured addresses for Phase 12.

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Rate model | **Store-defined methods:** <br>• a flat price in the store currency; <br>• an optional free-shipping threshold; <br>• optional countries (ISO codes); <br>• a delivery estimate (minimum and maximum days); <br>• a carrier with an https tracking-link template containing `{number}`. <br>The rules live in the `ShippingMethod` entity | Weight or zone tables: products have no weights yet. Live carrier APIs: no carrier integration exists yet, and the strategy interface below is where one would plug in |
| Where rates come from | **`IShippingRateProvider`**, the Shipping module's contract. Today it's `StoreShippingRates`, over the store's methods. Shopping's pricing pipeline asks it; Ordering takes the chosen option through the pipeline | The pricing service reading shipping tables directly: crosses a module boundary |
| Free-shipping threshold | **Measured on the goods total after the coupon discount** | Before the discount: a coupon could take the order under the threshold and it would still ship free |
| Stores without methods | **Free shipping with no choice**, exactly as before, so existing stores keep working | Blocking checkout until shipping is configured |
| Choosing a method | **Required when the store has active methods.** The problems are results in the quote and a `422` at checkout, before any write: <br>• `ShippingMethodRequired`: none was chosen; <br>• `ShippingMethodUnavailable`: the chosen one doesn't serve the address; <br>• `ShippingNotAvailable`: none serves it | Silently picking the cheapest |
| Destination country | **For the order, from the customer's address book on the server**, never sent by the client. The quote takes it from the client, because it's only a preview and checkout checks again. A typed address has no country, so only methods without country limits apply | A country field on the order request: claimed by the client |
| Order snapshot | **Method name, cost (in the order currency), delivery estimate, destination country, carrier and tracking-link template.** Total = subtotal − discount + shipping, frozen at placement. The payment intent and refunds use that total | A foreign key to the method: editing or deleting a method would rewrite history |
| Tracking link | Built from the snapshot template and the tracking number, URL-encoded. **https only** | Free-form links: a `javascript:` or plain-http link would reach customers |
| Carrier | The method's carrier becomes the shipment's default carrier. Staff may name another when shipping | Typing the carrier on every shipment |
| Deleting a method | Hard delete, since orders keep their snapshot. Deactivate to pause | Soft delete only: clutters the list |

## Decision

The options marked "Chosen" above. The migration is additive:
- a new `ShippingMethods` table;
- new snapshot columns on `Orders`. `ShippingAmount` defaults to 0 for existing rows; the others stay null.

Existing totals are unchanged. `MigrationRehearsalTests` checks it.

## Consequences

- **Positive:**
  - Checkout quotes and charges shipping.
  - Totals, the payment intent and refunds include shipping.
  - Customers see the method, its estimate and a carrier tracking link.
  - Stores without methods are unaffected.
  - A carrier API can be added behind the strategy without touching pricing or checkout.
- **Negative / limits:**
  - No weight- or zone-based rates, and no labels printed with carriers.
  - A typed address (no country) sees only methods without country limits. Customers choose a saved address for the others, and the checkout says so.
  - Estimates are informational: no cut-off times or business-day calendars.
  - A method has one name, in the store's language. Names per language can come with the white-label frontend (Phase 15) if needed.
  - Tax is still zero (P-06).

## Revisit when

- Products gain weights, or a store needs zone tables.
- A carrier integration (rates or labels) is requested.
- Tax (P-06) is decided, since it may need a structured address snapshot on the order.

## Verification

- **Domain:**
  - `ShippingMethodTests`: prices and the threshold; countries; tracking links; the rules, where a rejected update changes nothing.
  - `OrderShippingTests`: the total includes shipping and is frozen at placement; the carrier default; the tracking link; currency and country guards.
- **Application:**
  - `StoreShippingRatesTests`: the country filter, ordering, the threshold, old-currency methods skipped, and `StoreShips`.
  - `PricingServiceTests`: the chosen method priced after the discount; the three problems; stores without methods.
  - `CreateOrderHandlerTests`: a method is required, and the chosen one flows into the total, the payment intent and the snapshot.
  - `ShippingMethodHandlersTests`.
- **Integration:**
  - `ShippingTests`: options per country, the country read from the address book, the payment amount, the tracking link on the order and on the public page, the free-shipping threshold, a store without methods, admin create/update/delete, rule errors, and a customer getting 403.
  - `TenantIsolationTests`: the method routes.
  - `MigrationRehearsalTests`: legacy orders keep zero shipping.
