# Product variants — proposal and decisions required

> **Status:** a proposal, not built. Roadmap Phase 16 stays 🟡 because a shopper can't choose a product variant (size, colour, capacity…). This page establishes what that would take in Souq specifically, where the current code assumes one variant, what can be built without a product decision, and the smallest set of decisions that unblocks the rest. Names of things that don't exist yet are in *italics*.
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`. **Level:** L3. **Related:** [ADR-0025](../../11-ADR/0025-catalog-model.md) (D-21, the default variant), [ADR-0026](../../11-ADR/0026-inventory-reservations.md) (stock per variant), [ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md), [ADR-0029](../../11-ADR/0029-orders-lifecycle.md), [ChangeGuide.md](ChangeGuide.md#i-need-to-add-product-attributes-or-real-variant-options-size-colour).

## 1. The answer in brief

**Souq is halfway there by design, and the unfinished half is concentrated in a few known places.** D-21 made the *variant* the sellable unit in Phase 5. Stock, reservations, the stock ledger, low-stock events and basket lines have been keyed by variant since then. What is missing:

1. **A way to describe how variants differ** (options and their values, in every language). Today a product has exactly one variant, and nothing can create a second.
2. **Choosing a variant**: the storefront API exposes no variant list, and basket calls carry only a product id, so the server derives the default variant.
3. **Recording the variant on the order.** `OrderItem` stores the product id, name and price, but **no variant, no variant label and no SKU**, and `Order.AddItem` merges lines by product. With two variants of one product in one order, the second would merge into the first line at the first line's price. That's a correctness defect waiting for variants to exist.
4. **Admin and reporting keyed by product:** the inventory admin endpoints, the product form and the storefront projections all read "the default variant".

**Readiness:** the engineering groundwork (§11, phase V1) can start now without any product decision. The customer-visible feature needs **three product decisions** (§10).

## 2. What the code assumes today

The lifecycle traced end to end, with every place the one-variant assumption lives.

| Step | Where | The assumption |
|---|---|---|
| Product creation | `Product` constructor; `CreateProductCommand` (single `Price`, `CompareAtPrice`, `Sku`, `StockQuantity`); `IVariantStockInitializer` | The constructor is the **only** code that creates a `ProductVariant` (`isDefault: true`). Product-level `Price`, `CompareAtPrice` and `Sku` are shortcuts to `Product.DefaultVariant`. |
| Invariant and schema | `ProductVariantConfiguration` | A filtered unique index guarantees exactly one default variant per product; SKUs are unique per store. Variants have no status, no sort order and no descriptive attributes. |
| Admin product | `AdminProductDto`, `AdminProductListItemDto`, `frontend/src/pages/admin/ProductFormDrawer.jsx` | One SKU, price, compare-at price and stock figure per product |
| Storefront display | `CatalogQueries` (every projection reads `Variants.Where(v => v.IsDefault)`), `ProductDto` (one `Price`, one `StockQuantity`), `frontend/src/components/product/ProductCard.jsx`, `frontend/src/app/structuredData.js` (one offer price) | Price, compare-at, on-sale filter, price range filter and stock all come from the default variant |
| Wishlist | `WishlistQueries` | Price from the default variant. A wishlist entry is a product, which stays correct with variants. |
| Selection → basket | `AddBasketItemHandler` (`product.DefaultVariant.Id`); `SetBasketItemQuantityCommand` and the remove call keyed by **product** id; `frontend/src/api/client.js` (`addToBasket(productId)`, `/basket/items/{productId}`); `frontend/src/context/CartContext.jsx` | The server chooses the variant. The `Basket` aggregate is already variant-keyed: `BasketLine.VariantId`, unique per basket. |
| Pricing | `PricingLine` (`ProductId`, `Quantity`); `PricingService` prices `product.Price` and reports `product.DefaultVariant.Id` | The quote can't price a specific variant. `PricedLine` already carries a `VariantId`. |
| Checkout | `BasketCheckout` turns basket lines back into product-keyed `PricingLine`s; `CreateOrderHandler`; the optional client-sent `OrderLineInput` path | Variant identity is dropped between basket and quote, then re-derived as the default |
| Order | `Order.AddItem(productId, name, price, qty)` merges by `ProductId`; `OrderItem` has no variant columns; `OrderItemDto` | **The order doesn't record what was bought** beyond the product |
| Inventory | `InventoryItem` (`ProductId` + `VariantId`, unique per variant), `StockReservation`, `StockMovement`, `ReservationLine(VariantId, …)`, `IStockAvailability` | Correctly variant-keyed. The product-keyed parts are `InventoryRepository.GetForProductAsync` (joins the default variant), `AdjustStockCommand`, `GetStockMovementsQuery`, the `api/admin/inventory/{productId}` routes, `InventoryItemDto.Id` (a product id), and `frontend/src/pages/admin/Inventory.jsx` |
| Payment and refunds | `Payment`, `Refund` | Amount-based, no line references: **unaffected** |
| Promotions | `Coupon.EnsureUsable`, `Coupon.CalculateDiscount` on the subtotal | Coupons apply to the basket subtotal, never to a product: **unaffected** |
| Shipping | `ShippingMethod` | Priced per order, not per item or weight: **unaffected** |
| Reviews | `Review` proves purchase with an `OrderId` at product level | A review is about the product: **unaffected** |
| Notifications | `StockBecameLow` carries `VariantId`; the low-stock notification names the product only; order emails list `OrderItem.ProductName` | Staff can't tell which size ran low; emails can't say which size was bought |
| Reporting | `StoreReportQueries`: best sellers grouped by `ProductId` + `ProductName` (`TopProductDto`); category performance joins products; the stock snapshot counts `InventoryItems` rows | Best sellers stay meaningful per product. The stock KPIs would silently start counting **variants** instead of products. |

## 3. What a "variant" should mean in Souq

**A variant is a distinct thing a store sells and counts in stock under one product page.** It has its own price, its own stock, optionally its own SKU, and a customer must know which one they are buying. It is not:
- a free-text customisation (engraving, a note to the store);
- a bundle;
- a separate product.

**What Souq's stores actually vary by** — clothing size, colour, capacity (250 ml / 1 l), material, configuration (8 GB / 16 GB) and dimensions — all share one shape: **a few named dimensions, each with a short list of values, one value per dimension per variant.** None needs formulas, dependent options, per-option surcharges or free input. A universal configurator (conditional options, computed prices, customer-entered values) is not justified by any evidence in the repository or roadmap and is out of scope.

**A product with no meaningful variants** remains exactly what every product is today: no options and one default variant. It is the common case and must stay effortless: the admin form looks the same as now.

### Product versus variant

| Belongs to the **product** | Belongs to the **variant** |
|---|---|
| Name, description, SEO texts (per language); slug; category; brand; video; status (Draft/Active/Archived); the image gallery; reviews; wishlist entries | Price and compare-at price (store currency); SKU (optional, unique per store); stock and low-stock threshold (Inventory's `InventoryItem`, one per variant); active/inactive; display order; its option values; optionally one gallery image to show when selected |

## 4. Proposal

### 4.1 Domain model (Catalog module)

```
Product (aggregate root, rowversion)
 ├─ ProductTranslation*              (exists)
 ├─ ProductImage*                    (exists)
 ├─ ProductOption*        (new, ≤ 3)  position, per-language name
 │    └─ ProductOptionValue*          position, per-language value
 └─ ProductVariant*       (extended)  price, compare-at, SKU, IsDefault (exists)
                                       + IsActive, SortOrder, one value per option,
                                         optional image (a ProductImage of this product)
```

- *ProductOption* and *ProductOptionValue* are children of `Product`, created and changed only through it, like `ProductImage`. Their texts reuse the existing per-culture translation pattern (`CatalogTranslation`), so a store's enabled languages drive them.
- A variant's option values are stored as a small child set, *VariantOptionValue* (variant → value), plus a normalized **combination key** on the variant (the value ids in option order), so uniqueness can be enforced by the database, not only in memory.

**Invariants the aggregate guards** (all new Domain rules, each with a test):
1. At most 3 options and a bounded number of values per option; option names and values are unique per product per culture.
2. With no options, the product has exactly one variant (today's shape).
3. With options, every variant has exactly one value for every option, and no two variants share a combination.
4. Exactly one default variant (kept from D-21, so single-variant callers and older clients keep working); the default must be active.
5. A variant is **never deleted once it exists**: order lines, reservations, the ledger and basket lines reference it. It is deactivated. The same reasoning is why products are archived, not deleted.
6. Adding the first option to an existing product converts its current default variant into one of the new variants (the admin picks its values). It keeps its id, stock, ledger and sales history; nothing is recreated.
7. Removing an option or a value that active variants use is refused; deactivate the variants first.
8. Every variant's price follows `ProductVariant.SetPricing` (store currency, positive, compare-at above price).
9. **Sellability:** `Product.IsSellable` becomes "active, in an active category, and with at least one active variant". A variant is sellable if its product is and it is active. Stock is separate: an out-of-stock variant is sellable-but-unavailable, as today.

**Module ownership:** options, values and variants are **Catalog**. Stock stays **Inventory** (one `InventoryItem` per variant, opened through the existing port `IVariantStockInitializer` in the same transaction as the variant). Nothing moves between modules.

### 4.2 Module boundaries

No new module arrow is needed:
- **Shopping** already reaches Catalog's product aggregate for pricing (a recorded crossing), and asks Inventory for availability through `IStockAvailability`, which is variant-keyed.
- **Ordering** receives the variant's identity and snapshot text through the existing Shopping contract (`PricedLine`, which gains the label and SKU). It never touches Catalog.
- **Inventory → Catalog** stays the existing inverted port.
- **Notifications** already loads the product for the low-stock message and would read the variant's label from the same aggregate. That is the same crossing, possibly one more type counted by the ratchet in [ModuleDomainDependencies.md](../../02-ARCHITECTURE/ModuleDomainDependencies.md), decided in review.
- **Reporting** reads SQL in its own query service.

### 4.3 Database and migrations

All additive (`MigrationSafetyTests` must stay green without registering a destructive migration):

| Table | Change |
|---|---|
| *ProductOptions*, *ProductOptionTranslations*, *ProductOptionValues*, *ProductOptionValueTranslations* | New, tenant-owned, composite `(TenantId, Id)` keys and same-store foreign keys like every catalog child |
| *VariantOptionValues* | New: (*VariantId*, *OptionValueId*), same-store foreign keys, unique per variant and option |
| `ProductVariants` | Add *IsActive* (default true), *SortOrder*, *CombinationKey* (null for option-less products) with a unique index on product + key where the key isn't null, and *ImageId* (nullable, same-store foreign key) |
| `OrderItems` | Add *VariantId* (not null after backfill; same-store foreign key to `ProductVariants`, restrict delete), *VariantLabel* (nullable snapshot text) and *Sku* (nullable snapshot) |

**Existing data:**
- **Products:** unchanged. Every existing product already has exactly one default variant and no options, which is today's simple product.
- **Order lines:** *VariantId* can be backfilled **exactly**. Since `Phase5Catalog`, the `Product` constructor is the only code that has ever created a variant, so each product has had one and only one variant for its whole life; the migration joins each line to its product's default variant. This must be verified in the migration rehearsal (`MigrationRehearsalTests`) against a copy of real data before release.
- **Historical SKU and variant label:** they **can't** be reconstructed truthfully, because the SKU may have changed since the sale. They stay null and read as "not recorded". Filling in today's SKU would put invented data into historical orders.

### 4.4 API contract

Backward compatible by construction: a product with one variant behaves exactly as today for every existing call.

| Area | Change |
|---|---|
| Storefront `ProductDto` | Adds *options* (per-language names and values) and *variants* (id, value ids, price, compare-at, on-sale, available, active, image id). `Price` keeps its meaning for single-variant products; its meaning for multi-variant products is decision P-08b. `StockQuantity` becomes the sum of sellable variants (or is superseded by per-variant availability). |
| Basket | `POST /api/basket/items` accepts an optional *variantId*. It is required when the product has more than one active variant (`422` with a new stable code, e.g. *VariantRequired*) and must belong to that product (otherwise `404`). New variant-keyed routes for quantity and removal. The product-keyed routes remain valid only for single-variant products. |
| Quote and checkout | `PricingLine` gains the variant id. `BasketCheckout` passes basket lines' variant ids through instead of re-deriving them. The optional client-sent `OrderLineInput` gains an optional variant id with the same rule as the basket. |
| Orders | `OrderItemDto` adds *VariantId*, *VariantLabel*, *Sku* for the customer, the admin and the order email |
| Admin catalog | `AdminProductDto` adds options and variants. New sub-resource actions under `api/admin/products/{id}`: define options and values; add a variant; update a variant's price, compare-at and SKU; activate or deactivate it; set the default; reorder. All behind `catalog.manage` and `IAuditable`, like the existing catalog commands. The existing product `PUT` keeps editing the default variant's price and SKU for single-variant products and refuses for multi-variant ones (a stable code), so an old client can't silently edit the wrong variant. |
| Admin inventory | Variant-keyed routes for movements, adjustments and threshold (`inventory.view` / `inventory.manage`); the list returns one row per variant with its label. Product-keyed routes remain for single-variant products. |
| Reporting | `TopProductDto` stays per product; an optional per-variant breakdown. The stock snapshot names its unit (see §7). |

Every new route with an id needs its row in the isolation table of `tests/Souq.IntegrationTests/TenantIsolationTests.cs`, which fails the build otherwise. The generated [Endpoints.md](../../05-API/Endpoints.md) and [UseCases.md](../UseCases.md) are regenerated.

### 4.5 Frontend

| Surface | Change |
|---|---|
| Product page (`frontend/src/pages/ProductDetail.jsx`) | One accessible radio group per option (a labelled `fieldset`, keyboard-operable, right-to-left aware). Price, compare-at, availability and image follow the selection. Add-to-cart stays disabled until a complete, sellable combination is chosen, and the reason is shown. Unavailable combinations are presented according to decision P-08c. The selection lives in the URL (`?variant=`) so a link opens the same variant. |
| Cards, search, offers, wishlist | Price display, the on-sale badge, sort and price filter follow decision P-08b |
| Structured data (`frontend/src/app/structuredData.js`) | A product with several prices publishes a price range offer instead of one price |
| Cart, checkout, order pages, order email | Each line shows its variant label, from the snapshot on orders and the live label in the basket; basket calls go through `frontend/src/api/client.js` with the variant id (`frontend/src/features/basket/basketModel.js` already carries it) |
| Admin product form | Unchanged for a simple product. An "options" section; a variant table (label, price, compare-at, SKU, opening stock, active, default) with a "create the missing combinations" helper; deactivation through `useConfirmAction`. The Domain limits are published by the server, as the store settings editor does, rather than copied. |
| Admin inventory (`frontend/src/pages/admin/Inventory.jsx`) | One row per variant, labelled; adjustments per variant |
| Text | Option names and values come from the store's data in its enabled languages. The UI strings ("choose a size…", "sold out", *VariantRequired* messages) go in both locale files; labels inside sentences use the `bidi` formatter. |

### 4.6 Cart, order, payment and inventory consequences

- **Cart:** a line is a variant (already true). A deactivated variant or a product that lost all active variants makes its line `Sellable: false`, and checkout refuses it, exactly as an unpublished product does today.
- **Pricing:** the unit price is the variant's live price until the order is placed; nothing else in the pipeline changes. Coupons, shipping and the zero tax stage are per basket.
- **Order:** `Order.AddItem` takes the variant id, label and SKU and **merges only lines of the same variant**. The snapshot is taken in the store's default culture, consistent with `ProductName` (a known limitation: an order shows its line texts in one language). Historical orders keep their snapshot even if the variant's price, SKU or option values change later, or it is deactivated.
- **Inventory:** unchanged mechanics. Reserve, commit and release are already per variant, and two lines of the same variant already reserve their sum. Creating a variant opens its `InventoryItem` in the same transaction; a variant without stock can't be sold.
- **Payment and refunds:** unchanged; they are order amounts.
- **Concurrency:** variant and option edits go through the `Product` root, which carries a rowversion. Stock contention stays on each `InventoryItem`'s rowversion. The combination-key unique index makes duplicate combinations race-proof.
- **Idempotency:** unchanged; the duplicate-checkout question is still owner decision F-8.

### 4.7 Reporting consequences

- **Best sellers and category performance** stay per product. A product's units and revenue are the sum of its variants, and existing reports are unchanged. A per-variant breakdown ("which size sells") becomes possible from `OrderItems.VariantId`, but only from the migration onwards with labels, because historical lines have no label.
- **Stock snapshot:** today it counts inventory rows, which equal products. With variants it would count variants. The dashboard must say which it shows. Proposal: count **variants** (the unit that actually runs out) and label it so; a product-level "any variant out" figure can sit beside it. This is a display change, not a business rule. [Dashboards.md](../Reporting/Dashboards.md) is updated with it.
- **Platform statistics** count products and orders, not variants: unaffected.

### 4.8 Security and tenant isolation

- A client-sent variant id is resolved through the tenant-filtered repository (another store's id isn't found → `404`) **and** must belong to the product in the same request. A mismatched pair must never price one product with another's variant.
- New tables are tenant-owned, with the query filter, the write guard, and same-store composite foreign keys, which `TenancyRuleTests` requires.
- Admin variant and option commands require `catalog.manage` and are `IAuditable` like the rest of the catalog; variant stock requires the inventory permissions. No new permission is needed.
- Option texts are plain text, required and length-limited like product names (`CatalogText`). No HTML. Product texts don't reject control characters today (store names do); because option values will appear inside order lines and emails, the ADR should decide whether option texts adopt the store-name rule.
- Limits (options per product, values per option, variants per product) are enforced in the Domain and the validators, so an admin form can't create an unbounded matrix.

## 5. Test strategy

| Level | What must be proven |
|---|---|
| Domain | Every invariant in §4.1: option and value limits, unique names per culture, one value per option per variant, unique combinations, one active default, no deletion, conversion of the existing default variant, refusal to remove used values, pricing rules per variant, sellability with zero active variants; `Order.AddItem` merges by variant and keeps each variant's price; snapshot immutability after placement |
| Application | Add-to-basket with and without a variant id, mismatched product/variant, inactive variant; the quote prices the chosen variant; checkout keeps variant identity from basket to order; creating a variant opens its stock in the same transaction; single-variant products keep today's behaviour on every old call (`CreateProductHandlerTests`, `PricingServiceTests`, `BasketTests` updated, not weakened) |
| Architecture | No new module arrow; new request types carry no `TenantId`; new admin commands are audited; `ModuleAndContractRuleTests` and the ratchet show only the reviewed change; the generated inventories are regenerated (`GeneratedDocsTests`) |
| Integration (real SQL Server) | Two variants of one product in one order: two lines, two reservations, correct totals; the last unit of one variant sells once under concurrency while the other variant is unaffected; a duplicate combination is refused even when two admins race; the migration backfills every existing order line with the right variant (`MigrationRehearsalTests`); catalog lists keep a fixed query count with many variants (extend `CatalogTests`); cross-store variant ids answer `404` (`TenantIsolationTests` rows for every new route); dashboards' numbers with variants (`StoreDashboardTests`) |
| Frontend (Vitest) | Pure selection logic (complete combination, availability per value, price range) in a typed `.js` feature module; the product page's selector behaviour and messages; the admin variant table's validation mirrors the published limits; cart and order lines show labels; axe on the product page with options |
| Browser (Playwright) | A shopper chooses a size in Arabic and in English, adds it, checks out, and sees the size on the order and in the admin; an unavailable size can't be bought; an admin converts a simple product into a two-option product and adjusts one variant's stock; the phone layout of the selector; dark mode contrast |

## 6. Documentation and ADR changes

- **A new ADR** (the next free number) superseding the *sellable unit* row of [ADR-0025](../../11-ADR/0025-catalog-model.md) with the option model, and recording the order-line snapshot decision and the backfill evidence
- [BusinessRules.md](../../01-REQUIREMENTS/BusinessRules.md): BR-CAT-01 replaced, with new catalog rules for options, combinations and sellability; the basket and order rules gain the variant (line merge, snapshot)
- Module documents:
  - [Catalog](README.md) and this module's [ChangeGuide.md](ChangeGuide.md) (the variants section becomes a pointer to the ADR);
  - [Inventory](../Inventory/README.md) (variant-keyed admin);
  - [Shopping](../Shopping/README.md);
  - [Ordering](../Ordering/README.md) (the snapshot);
  - [Reporting](../Reporting/README.md) and [Dashboards.md](../Reporting/Dashboards.md) (the stock unit).
- [FeatureMaps.md](../FeatureMaps.md), [FrontendGuide.md](../../08-FRONTEND/FrontendGuide.md), [ApiDocumentation.md](../../05-API/ApiDocumentation.md) (the new error codes)
- The generated inventories, and the roadmap's Phase 16 line when it is delivered

## 7. Risks

| Risk | Mitigation |
|---|---|
| An old client edits the price of the wrong variant | The product `PUT` refuses multi-variant products with a stable code |
| An old client adds a multi-variant product without a variant | *VariantRequired* instead of silently choosing the default |
| The order backfill guesses wrong | It is exact only because every product has one variant; the rehearsal on a copy of real data proves it before release, and the migration aborts if any product has more than one variant at that moment |
| Catalogue queries slow down with many variants | Per-variant subqueries stay indexed by product; the query-count test is extended; the limits bound the matrix |
| Dashboards silently change meaning | The stock KPI is relabelled in the same change (§4.7) |
| Scope creep into a configurator | The three-option shape is written into the ADR; anything else needs a new decision |

## 8. What can be decided and built by engineering, and what can't

**Safe without an owner decision** (no commercial behaviour changes, correct under any answer below):
- recording the variant on order lines (id, label and SKU snapshots) and merging lines by variant, with the exact backfill;
- carrying the variant id end to end through the basket, quote and checkout contracts, still resolving the default when none is sent, so behaviour is identical for today's products;
- variant-keyed inventory repository methods and admin routes beside the product-keyed ones;
- the ADR draft, the new Domain rules for variant lifecycle (active flag, no deletion), and the tests.

**Needs a product decision:** anything a shopper or a merchant sees differently — the option model, how prices are shown, and how unavailable choices behave (§10).

## 9. Engineering defaults (proposed; the owner may override)

| Question | Default | Why a default is acceptable |
|---|---|---|
| Variant images | A variant may point at one image already in the product's gallery; no separate uploads | Uses the existing gallery and its limits; can be left out of the first release |
| Limits | At most 3 options per product, 20 values per option, 100 variants per product | Covers size × colour × material with room; protects queries and the admin form. It is a limit, so it is listed in [OwnerDecisions.md](../../09-OPERATIONS/OwnerDecisions.md) for confirmation. |
| SKU | Optional per variant, unique per store (today's rule) | No change to BR-CAT-04 |
| Stock KPI unit | Variants, labelled as such | §4.7 |
| Language of order-line snapshots | The store's default culture, like `ProductName` | Consistent with today; per-language snapshots would be a separate decision |
| Wishlist | Stays per product | A shopper wishes for the product; the size is chosen at purchase |

## 10. Decisions required (P-08)

Three answers unblock the customer-visible feature. Everything else has a default above.

| # | Question | Options | Recommendation |
|---|---|---|---|
| **P-08a** | **How do variants differ?** | (a) Structured options: up to three named dimensions (size, colour, capacity…), each with translated values; a variant is one value per dimension · (b) Free-form variants: each variant only has a translated label, no dimensions · (c) Both | **(a).** It covers every store type considered (size, capacity, material, colour, configuration, dimensions); it allows one selector per dimension and later filtering by value; a single dimension named "Option" covers the free-form case. (b) is simpler to administer, but cannot show "which sizes exist in red", and migrating from (b) to (a) later means re-entering every variant. |
| **P-08b** | **What price does a multi-variant product show** on cards, in search, sorting, price filters, the on-sale badge and structured data? | (a) The default variant's price · (b) "From" the lowest price among sellable variants; the filter matches if any sellable variant is in range; on sale if any sellable variant is · (c) A price range | **(b)** for cards and sorting, **(c)** in structured data. A shopper filtering "under 20" should find a product whose small size costs 15, and "from" is honest when prices differ. (a) can show a price the shopper can't buy if the default is sold out. This is commercial display, so it is the owner's. |
| **P-08c** | **Selection and availability behaviour** | Preselect the default variant, or require an explicit choice? Show sold-out and inactive combinations disabled, or hide them? What happens when no combination is available: show "unavailable", or hide the product? | Require an explicit choice when there is more than one active variant (a preselected wrong size is a costly return); show sold-out values disabled with a "sold out" label; hide inactive ones; a product with no sellable variant stays visible as "unavailable", as an out-of-stock single-variant product is today. |

**Also for confirmation:** the limits in §9.

## 11. Phased implementation plan

| Phase | Scope | Needs |
|---|---|---|
| **V0** | Record P-08; write the ADR; update BusinessRules | Owner answers to P-08a–c |
| **V1 — groundwork** (no visible change) | Order lines record variant id, label and SKU snapshots and merge by variant; the migration with exact backfill and its rehearsal; variant ids carried through the basket, quote and checkout contracts with default resolution; variant-keyed inventory repository and admin routes beside the product-keyed ones; variant active flag and no-delete rule in the Domain; tests at every level; documents | Nothing; can start now |
| **V2 — catalog model and admin** | Options, values, combinations and limits in the Domain; migrations; admin API and the options and variant table in the product form; variant stock opened on creation; admin inventory per variant; low-stock notification names the variant | P-08a, the limits |
| **V3 — storefront and checkout** | `ProductDto` options and variants; the selector; basket with *VariantRequired*; cart, checkout, order pages and email show labels; price display, filters, on-sale and structured data per P-08b; availability per P-08c; browser journeys in both languages | P-08b, P-08c |
| **V4 — reporting and hardening** | Per-variant best-seller breakdown; relabelled stock KPI; query-count and concurrency tests with many variants; release rehearsal of the migration on a copy of production data | — |

Each phase leaves the repository releasable. After V1 nothing is visible to shoppers or merchants; after V2 a merchant can define variants while the storefront still sells only single-variant products (multi-variant products stay hidden from sale until V3). Decide during V2 whether they are refused as unsellable or kept in Draft.
