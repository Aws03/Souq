# Product variants — decisions, status and plan

> **Status:** **P-08 decided** (2026-09-17). **V1 (groundwork) built**: the order records the variant, and basket, pricing, checkout and stock carry it ([ADR-0039](../../11-ADR/0039-product-variants-order-identity.md)). **V2 (the option model and merchant admin) built** ([ADR-0040](../../11-ADR/0040-product-option-model.md)): merchants define options and values and manage variants as combinations. **V3 (the storefront selection) is not built**, so roadmap Phase 16 stays 🟡: a shopper still can't choose a size, colour or capacity, and until V3 a product with more than one active variant is not shown in the storefront. This page traced where the code assumed one variant, proposed the model, and now records what is done and what remains. Names of things that don't exist yet are in *italics*.
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`. **Level:** L3. **Related:** [ADR-0039](../../11-ADR/0039-product-variants-order-identity.md) (P-08 and V1), [ADR-0040](../../11-ADR/0040-product-option-model.md) (the option model, V2), [ADR-0025](../../11-ADR/0025-catalog-model.md) (D-21, the default variant), [ADR-0026](../../11-ADR/0026-inventory-reservations.md) (stock per variant), [ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md), [ADR-0029](../../11-ADR/0029-orders-lifecycle.md), [ChangeGuide.md](ChangeGuide.md#i-need-to-add-product-attributes-or-real-variant-options-size-colour).

## 1. The answer in brief

**Souq was halfway there by design.** D-21 made the *variant* the sellable unit in Phase 5: stock, reservations, the stock ledger, low-stock events and basket lines have been keyed by variant since then. The investigation found four gaps. V1 has closed two and prepared the other two:

1. **Recording the variant on the order — done in V1.** `OrderItem` now stores `VariantId` plus `VariantLabel` and `Sku` snapshots, and `Order.AddItem` merges lines by variant. Before this, two variants in one order would have merged into one line at the first variant's price. Existing lines were linked to their exact variant; their SKU and label stay empty rather than invented.
2. **Carrying a chosen variant through checkout — done in V1 on the server.** `PricingLine`, the basket and the order commands carry an optional variant id; the server refuses another product's variant and a deactivated one, and answers `VariantRequired` instead of assuming the default. The storefront still sends product ids only (V3).
3. **A way to describe how variants differ — done in V2.** Structured options per P-08a: `Product.SetOptions` defines up to 3 options with up to 20 values each (Arabic and English names), and `Product.AddVariant` creates a variant from one value of every option, up to 100 per product. The admin page `/admin/products/:productId/variants` manages them.
4. **Admin and reporting keyed by product — admin done in V1 and V2, reporting in V4.** Stock administration, the product admin and the inventory screen work per variant. The storefront projections still read the default variant (V3), and the dashboards count inventory rows, which are now variants (V4).

**Readiness:** V3 can start once the owner confirms two presentation questions (§10); nothing else blocks it.

## 2. What the code assumes today

The lifecycle as traced before V1, with every place the one-variant assumption lived. Rows marked **V1** have since been changed ([ADR-0039](../../11-ADR/0039-product-variants-order-identity.md)); the rest still hold.

| Step | Where | The assumption |
|---|---|---|
| Product creation | `Product` constructor; `CreateProductCommand` (single `Price`, `CompareAtPrice`, `Sku`, `StockQuantity`); `IVariantStockInitializer` | A new product is still created simple (one default variant). **V2:** options and more variants are added afterwards (`SetProductOptionsCommand`, `CreateProductVariantsCommand`, which opens each variant's stock through the same port). Product-level `Price`, `CompareAtPrice` and `Sku` stay shortcuts to `Product.DefaultVariant`; on a product with options the product `PUT` refuses to change them (`ProductHasVariants`). |
| Invariant and schema | `ProductVariantConfiguration` | A filtered unique index guarantees exactly one default variant per product; SKUs are unique per store. Variants have no status, no sort order and no descriptive attributes. |
| Admin product | **V2:** `AdminProductDto` carries `Options`, `Variants` (per-variant stock) and `VariantLimits`; `AdminProductListItemDto.VariantCount`; `frontend/src/pages/admin/ProductVariants.jsx`; `frontend/src/pages/admin/ProductFormDrawer.jsx` links to it and locks pricing on a product with options | Resolved |
| Storefront display | `CatalogQueries` (every projection reads `Variants.Where(v => v.IsDefault)`), `ProductDto` (one `Price`, one `StockQuantity`), `frontend/src/components/product/ProductCard.jsx`, `frontend/src/app/structuredData.js` (one offer price) | Price, compare-at, on-sale filter, price range filter and stock all come from the default variant. **V2 gate (temporary):** `CatalogQueries` and `WishlistQueries` show a product only while it has exactly one active variant, and stock sums active variants — so the default-variant reading is exact for everything shown. V3 lifts the gate |
| Wishlist | `WishlistQueries` | Price from the default variant. A wishlist entry is a product, which stays correct with variants. |
| Selection → basket | **V1:** `AddBasketItemHandler` takes an optional variant id (`Product.FindVariant`, `Product.CanSell`, `Product.ImplicitVariant`); `SetBasketLineQuantityCommand` and `RemoveBasketLineCommand` edit a line by variant; the product-keyed commands answer `VariantRequired` when ambiguous. **Still:** `frontend/src/api/client.js` (`addToBasket(productId)`, `/basket/items/{productId}`), `frontend/src/context/CartContext.jsx` | The storefront still lets the server choose, which is correct while every product has one variant |
| Pricing | **V1:** `PricingLine` carries an optional `VariantId`; `PricingService` prices that variant (or the implicit one) and reports its SKU; `PricedLine` gains `VariantLabel`, `Sku`, `VariantRequired` | Resolved |
| Checkout | **V1:** `BasketCheckout` passes each basket line's variant id and consumes per variant; `OrderLineInput` accepts an optional variant id; `CreateOrderHandler` answers `VariantRequired` | Resolved |
| Order | **V1:** `Order.AddItem` takes the variant, label and SKU and merges by variant; `OrderItem` and `OrderItemDto` carry them. **Still:** order pages and emails don't show the label or SKU | Resolved in the model; display is V3 |
| Inventory | `InventoryItem` (`ProductId` + `VariantId`, unique per variant), `StockReservation`, `StockMovement`, `ReservationLine(VariantId, …)`, `IStockAvailability`. **V1:** `IInventoryRepository.GetForVariantAsync`, `AdjustVariantStockCommand`, `SetVariantLowStockThresholdCommand`, `GetVariantStockMovementsQuery`, `api/admin/inventory/variants/{variantId}/…`; `InventoryItemDto.VariantId`; one list row per variant | Correctly variant-keyed. **V2:** `InventoryItemDto` carries `VariantLabel` and `VariantIsActive`; `frontend/src/pages/admin/Inventory.jsx` shows one labelled row per variant and uses the variant routes |
| Payment and refunds | `Payment`, `Refund` | Amount-based, no line references: **unaffected** |
| Promotions | `Coupon.EnsureUsable`, `Coupon.CalculateDiscount` on the subtotal | Coupons apply to the basket subtotal, never to a product: **unaffected** |
| Shipping | `ShippingMethod` | Priced per order, not per item or weight: **unaffected** |
| Reviews | `Review` proves purchase with an `OrderId` at product level | A review is about the product: **unaffected** |
| Notifications | `StockBecameLow` carries `VariantId`. **V2:** `StockBecameLowHandler` adds `variantId` and `variantLabel` (from `Product.VariantLabel`), shown by `frontend/src/features/notifications/notificationView.js`. **Still:** order emails list `OrderItem.ProductName` only | Emails can't say which size was bought (V3) |
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

- `ProductOption` and `ProductOptionValue` are children of `Product`, created and changed only through it (`Product.SetOptions`), like `ProductImage`. Their names follow the per-culture translation pattern with a lighter base, `OptionTranslation` (name only), so a store's languages drive them. *(As built in V2.)*
- A variant's option values are stored as a small child set, `ProductVariantOptionValue` (variant → value), plus a normalized **combination key** on the variant. As built it is the values' stable `Key`s sorted, not their database ids, so a value and its variant can be created in one save and a reorder changes nothing; the database enforces its uniqueness ([ADR-0040](../../11-ADR/0040-product-option-model.md)).

**Invariants the aggregate guards** (all new Domain rules, each with a test):
1. At most 3 options and a bounded number of values per option; option names and values are unique per product per culture.
2. With no options, the product has exactly one variant (today's shape).
3. With options, every variant has exactly one value for every option, and no two variants share a combination.
4. Exactly one default variant (kept from D-21, so single-variant callers and older clients keep working); the default must be active.
5. A variant is **never deleted once it exists**: order lines, reservations, the ledger and basket lines reference it. It is deactivated. The same reasoning is why products are archived, not deleted.
6. Adding the first option to an existing product converts its current default variant into one of the new variants (the admin picks its values). It keeps its id, stock, ledger and sales history; nothing is recreated.
7. Removing a value that any variant uses (active or not) is refused — deactivate the variant instead; removing an option is refused if two combinations would collide. *(As built: stricter than first proposed, because variants are never deleted.)*
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
| `ProductOptions`, `ProductOptionTranslations`, `ProductOptionValues`, `ProductOptionValueTranslations` | **Built in V2**: tenant-owned aggregate children; `ProductOptionValues` has the alternate key (`TenantId`, `Id`); one translation per owner and culture |
| `ProductVariantOptionValues` | **Built in V2**: (`ProductVariantId`, `OptionValueId`) unique; same-store foreign key to the value, **restrict** |
| `ProductVariants` | `IsActive` (default true) with `CK_ProductVariants_DefaultIsActive` — **built in V1**. `CombinationKey` (null for option-less products) with a unique index on product + key where the key isn't null — **built in V2** (`ProductOptionsAndVariants`). *SortOrder* and *ImageId* are **not built** (variants are listed in option-value order; no variant images) |
| `OrderItems` | **Built in V1** (`OrderLinesRecordVariant`): `VariantId` (not null after the backfill; same-store foreign key to `ProductVariants`, restrict), `VariantLabel` and `Sku` (nullable snapshots), unique (`OrderId`, `VariantId`) |

**Existing data:**
- **Products:** unchanged. Every existing product already has exactly one default variant and no options, which is today's simple product.
- **Order lines (done in V1):** `VariantId` was backfilled **exactly**. Since `Phase5Catalog`, the `Product` constructor is the only code that has ever created a variant, so each product has had one and only one variant for its whole life; the migration joins each line to its product's default variant. `MigrationRehearsalTests` proves it on legacy rows, and proves the migration aborts when a non-default variant exists. Rehearse it on a copy of production data before release.
- **Historical SKU and variant label:** they **can't** be reconstructed truthfully, because the SKU may have changed since the sale. They stay null and read as "not recorded". Filling in today's SKU would put invented data into historical orders.

### 4.4 API contract

Backward compatible by construction: a product with one variant behaves exactly as today for every existing call.

| Area | Change |
|---|---|
| Storefront `ProductDto` | Adds *options* (per-language names and values) and *variants* (id, value ids, price, compare-at, on-sale, available, active, image id). `Price` keeps its meaning for single-variant products; its meaning for multi-variant products is decision P-08b. `StockQuantity` becomes the sum of sellable variants (or is superseded by per-variant availability). |
| Basket | **Built in V1:** `POST /api/basket/items` accepts an optional `variantId`, required when the product has more than one active variant (`422 VariantRequired`) and belonging to that product (otherwise `404`); `PUT`/`DELETE api/basket/items/variants/{variantId}`; the product-keyed routes answer `VariantRequired` when the product has more than one line |
| Quote and checkout | **Built in V1:** `PricingLine` carries the variant id; `BasketCheckout` passes it through; `OrderLineInput` accepts an optional `variantId` with the basket's rule |
| Orders | **Built in V1:** `OrderItemDto` has `VariantId`, `VariantLabel`, `Sku`. The order email and the order screens show them in V3 |
| Admin catalog | **Built in V2:** `AdminProductDto` adds `Options`, `Variants` and `VariantLimits`. Under `api/admin/products/{id}`: `PUT options` (the whole definition), `POST variants` (a batch), `PUT variants/{variantId}`, `PUT variants/{variantId}/status`, `PUT variants/{variantId}/default`, all behind `catalog.manage` and audited. The product `PUT` edits the default variant's pricing for a simple product and refuses a change on a product with options (`ProductHasVariants`). Reordering variants is not built |
| Admin inventory | **Built in V1:** `api/admin/inventory/variants/{variantId}/movements\|adjustments\|threshold` (`inventory.view` / `inventory.manage`, audited with target `ProductVariant`); the list returns one row per variant with `VariantId` and its own SKU; the product-keyed routes answer `VariantRequired` for a product with more than one stock row. **V2:** each row carries `VariantLabel` and `VariantIsActive` |
| Reporting | `TopProductDto` stays per product; an optional per-variant breakdown. The stock snapshot names its unit (see §7). |

Every new route with an id needs its row in the isolation table of `tests/Souq.IntegrationTests/TenantIsolationTests.cs`, which fails the build otherwise. The generated [Endpoints.md](../../05-API/Endpoints.md) and [UseCases.md](../UseCases.md) are regenerated.

### 4.5 Frontend

| Surface | Change |
|---|---|
| Product page (`frontend/src/pages/ProductDetail.jsx`) | One accessible radio group per option (a labelled `fieldset`, keyboard-operable, right-to-left aware). Price, compare-at, availability and image follow the selection. Add-to-cart stays disabled until a complete, sellable combination is chosen, and the reason is shown. Unavailable combinations are presented according to decision P-08c. The selection lives in the URL (`?variant=`) so a link opens the same variant. |
| Cards, search, offers, wishlist | Price display, the on-sale badge, sort and price filter follow decision P-08b |
| Structured data (`frontend/src/app/structuredData.js`) | A product with several prices publishes a price range offer instead of one price |
| Cart, checkout, order pages, order email | Each line shows its variant label, from the snapshot on orders and the live label in the basket; basket calls go through `frontend/src/api/client.js` with the variant id (`frontend/src/features/basket/basketModel.js` already carries it) |
| Admin product form | **Built in V2** as its own page, `frontend/src/pages/admin/ProductVariants.jsx` (linked from the product form and the product list), because the matrix needs room and a URL survives a reload: the options editor (`ProductOptionsEditor.jsx`), a variant table (label, SKU, price, stock, status, default; edit, activate/deactivate through `useConfirmAction`, make default, adjust stock, history) and "create variants" for missing combinations (`CreateVariantsPanel.jsx`). The limits come from the server (`VariantLimits`). The product form is unchanged for a simple product |
| Admin inventory (`frontend/src/pages/admin/Inventory.jsx`) | **Built in V2:** one row per variant, labelled, inactive variants marked; adjustments and history per variant through `frontend/src/pages/admin/StockDrawers.jsx` |
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

- **Done in V1:** [ADR-0039](../../11-ADR/0039-product-variants-order-identity.md) supersedes the *sellable unit* row of [ADR-0025](../../11-ADR/0025-catalog-model.md), records P-08, the order-line snapshot and the backfill evidence; [BusinessRules.md](../../01-REQUIREMENTS/BusinessRules.md) BR-CAT-01 replaced and BR-CAT-18, BR-BSK-14, BR-ORD-31, BR-INV-12 added, with BR-BSK-08, BR-ORD-06 and BR-ORD-08 gaining the variant. **Done in V2:** [ADR-0040](../../11-ADR/0040-product-option-model.md) records the option model (limits, combination key, control characters rejected in option texts, the storefront gate) and BusinessRules gains BR-CAT-19 to BR-CAT-24
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

## 8. What V1 and V2 built, and what they deliberately didn't

**Built (no visible change for shoppers or merchants; [ADR-0039](../../11-ADR/0039-product-variants-order-identity.md)):**
- order lines record the variant with label and SKU snapshots and merge by variant, with the exact backfill and its abort guards (`OrderLinesRecordVariant`);
- the variant id carried end to end through the basket, the quote and checkout, with the implicit variant for a product with one active variant and `VariantRequired` otherwise;
- another product's, another store's or a deactivated variant refused;
- variant-keyed stock administration beside the product-keyed routes;
- `ProductVariant.IsActive`, `Product.DeactivateVariant` (the default can't be deactivated) and no deletion;
- tests at every level, including `ProductVariantTests` on SQL Server with two real variants of one product.

**V2 built ([ADR-0040](../../11-ADR/0040-product-option-model.md)):**
- options and values with Arabic and English names, unique per product and option in each language, no control characters, within the published limits (3 options, 20 values, 100 variants — inactive ones count);
- variants as combinations with a database-unique `CombinationKey`; the first option converts the existing default variant in place; a new option names the value existing variants take;
- a value used by any variant can't be removed; an option can be removed only if combinations stay unique;
- per-variant price, compare-at and SKU; activate and deactivate; a movable default that must be active; product-level pricing refused on a product with options;
- a root concurrency guard (`IProductRepository.GuardConcurrentEdit`) for every structural edit;
- the label snapshot on order lines and pricing lines, the variant label in the low-stock notification, and per-variant rows in the inventory list;
- the merchant page `/admin/products/:productId/variants`, the per-variant inventory screen, Arabic and English strings for every new error code;
- the temporary storefront gate (a product is shown only with one active variant);
- tests at every level, including `ProductOptionAdminTests` on SQL Server and the browser journey `frontend/e2e/product-variants.spec.js`.

**Not built, on purpose:** `ProductDto` options and variants, the storefront picker, "From" pricing, disabled sold-out values, labels in cart, checkout, order screens and email (V3); variant images and manual variant ordering; reporting per variant and the relabelled stock KPI (V4).

## 9. Engineering defaults (proposed; the owner may override)

| Question | Default | Why a default is acceptable |
|---|---|---|
| Variant images | A variant may point at one image already in the product's gallery; no separate uploads | Uses the existing gallery and its limits. **Left out of V2**; the storefront picker (V3) is the first place it would matter |
| Limits | At most 3 options per product, 20 values per option, 100 variants per product | **Confirmed by the owner** with P-08 (2026-09-17). Covers size × colour × material with room; protects queries and the admin form. |
| SKU | Optional per variant, unique per store (today's rule) | No change to BR-CAT-04 |
| Stock KPI unit | Variants, labelled as such | §4.7 |
| Language of order-line snapshots | The store's default culture, like `ProductName` | Consistent with today; per-language snapshots would be a separate decision |
| Wishlist | Stays per product | A shopper wishes for the product; the size is chosen at purchase |

## 10. Decisions (P-08) — decided 2026-09-17

| # | Question | Decision |
|---|---|---|
| **P-08a** | How do variants differ? | **Structured named options**: at most 3 options per product, 20 values per option, 100 variants per product |
| **P-08b** | What price does a multi-variant product show on cards, in search, sorting, price filters, the on-sale badge and structured data? | **"From" the lowest price among variants that can currently be purchased.** Engineering reading for V3, consistent with the proposal: the price filter matches if any purchasable variant is in range, the product is on sale if any purchasable variant is, and structured data publishes a price range |
| **P-08c** | Selection and availability behaviour | **An explicit choice is required; sold-out options stay visible but disabled** |

**Still to confirm when V3 is built.** The answers don't cover two presentation defaults from the original recommendation: hiding *deactivated* variants rather than showing them disabled, and keeping a product with no purchasable variant listed as "unavailable". Neither blocks V2. The options originally considered are recorded in [ADR-0039](../../11-ADR/0039-product-variants-order-identity.md).

## 11. Phased implementation plan

| Phase | Scope | Status |
|---|---|---|
| **V0** | Record P-08; write the ADR; update BusinessRules | ✅ Done — [ADR-0039](../../11-ADR/0039-product-variants-order-identity.md), BR-CAT-01/18, BR-BSK-08/14, BR-ORD-06/08/31, BR-INV-12 |
| **V1 — groundwork** (no visible change) | Order lines record variant id, label and SKU snapshots and merge by variant; the migration with exact backfill and its rehearsal; variant ids carried through the basket, quote and checkout contracts with implicit-variant resolution and `VariantRequired`; variant-keyed inventory repository and admin routes beside the product-keyed ones; variant active flag and no-delete rule in the Domain; tests at every level; documents | ✅ Done (§8) |
| **V2 — catalog model and admin** | Options, values, combinations and limits in the Domain (`AddVariant` public with option values); migrations; admin API and the options and variant page; the product `PUT` refusing price edits on a product with options; variant stock opened on creation; the label snapshot composed from options; the admin inventory screen per variant; the low-stock notification names the variant | ✅ Done (§8, [ADR-0040](../../11-ADR/0040-product-option-model.md)) |
| **V3 — storefront and checkout** | `ProductDto` options and variants; the selector with an explicit choice and disabled sold-out values; variant-keyed basket calls from the storefront; cart, checkout, order pages and email show labels; "From" pricing in cards, search, sort, filters, on-sale and a price range in structured data; browser journeys in both languages | Not started; needs the two confirmations in §10. Also removes the V2 storefront gate |
| **V4 — reporting and hardening** | Per-variant best-seller breakdown; relabelled stock KPI; query-count and concurrency tests with many variants; release rehearsal of the migration on a copy of production data | Not started |

Each phase leaves the repository releasable. After V1 nothing was visible to shoppers or merchants. **After V2 (now)** a merchant can define variants while the storefront still sells only single-variant products: V2 decided that a product with more than one active variant stays **Active but is not shown in the storefront** (rather than refused or kept in Draft), so a merchant can prepare inactive variants on a selling product without unpublishing it ([ADR-0040](../../11-ADR/0040-product-option-model.md)).
