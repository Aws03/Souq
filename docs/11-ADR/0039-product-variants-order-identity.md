# ADR-0039: Product variants: the order records the variant, checkout carries it, and the option model is decided

- **Status:** Accepted, 2026-09-17. The groundwork phase V1 is implemented. The option model (V2) is built by [ADR-0040](0040-product-option-model.md); the storefront selection (V3) is decided here but not built. Supersedes in part [ADR-0025](0025-catalog-model.md): its *sellable unit* row said every product has exactly one variant, and that an option matrix was out of scope.
- **Date:** 2026-09-17
- **Related modules:** Catalog; Shopping; Ordering; Inventory
- **Related ADRs:** supersedes in part [ADR-0025](0025-catalog-model.md) (D-21, the default variant); keeps stock per variant from [ADR-0026](0026-inventory-reservations.md) and adds variant addressing to its admin paths; changes the line shape of the pricing pipeline in [ADR-0028](0028-basket-and-pricing-pipeline.md); adds the variant to the frozen order lines of [ADR-0029](0029-orders-lifecycle.md); new failures use the error contract of [ADR-0017](0017-error-contract.md); composite same-store keys per [ADR-0022](0022-tenancy-enforcement.md)

## Context

Since Phase 5 the **variant** has been the sellable unit (D-21). Stock, reservations, the ledger and basket lines are keyed by variant. But every product has exactly one variant, and nothing can create a second. [ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md) traced the one-variant assumption through the code and asked three product questions. The owner answered them (decision P-08, 2026-09-17):

- **P-08a, how variants differ:** structured named options, at most **3 options** per product, **20 values** per option and **100 variants** per product.
- **P-08b, the price of a multi-variant product in lists:** "**From**" the lowest price among variants that can currently be purchased.
- **P-08c, choosing a variant:** an **explicit choice** is required; sold-out options stay **visible but disabled**.

These answers shape V2 and V3. This record also decides the V1 groundwork, which is correct under any answer and changes nothing a shopper or merchant sees today.

## Problem

Four places made a second variant unsafe before a single option could be defined:

1. **The order didn't record what was bought.** `OrderItem` stored the product, its name and price only, and `Order.AddItem` merged lines **by product**. Two sizes in one order would have collapsed into one line at the first size's price. That is an invoice that disagrees with what was charged for stock.
2. **Checkout dropped the variant.** `BasketCheckout` turned variant-keyed basket lines back into product-keyed `PricingLine`s, and `PricingService` priced `product.DefaultVariant`. A basket holding the large size would have been ordered, priced and reserved as the small one.
3. **The API had no way to name a variant,** and product-keyed basket and stock routes would silently have acted on the default.
4. **Historical order lines** had no variant, and any backfill must not invent data.

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| What an order line records | The **variant id** (a reference; variants are never deleted), plus **snapshots** of the variant label and SKU at purchase | A snapshot without the id: reporting "which size sells" would join on text. The product id alone: the current defect |
| Backfilling existing order lines | **Join each line to its product's default variant**, guarded. The link is certain, not a guess: since `Phase5Catalog` only the `Product` constructor has ever created a variant, so every product had one variant for its whole life. The migration aborts (and its transaction rolls back) if any non-default variant exists, if a line finds no variant, or if an order has two lines for one product | A nullable `VariantId` forever: every consumer would carry a "maybe no variant" branch for a case that doesn't exist. Filling the historical SKU and label from today's catalog: the SKU may have changed since the sale, so that would put invented data on invoices. **They stay null** and read as "not recorded" |
| How lines merge in an order | **By variant.** The same variant twice becomes one line. Two variants of one product become two lines, each at its own price. Adding a variant under a different product, or at a different unit price, is **refused** rather than merged | Merging by product: the defect. Silently keeping the first price on a merge: that hid a contradiction |
| A basket or order line that names no variant | The product's **implicit variant**: its only active variant, which is the default for every product today. If the product has **more than one active variant**, the request is refused with `422 VariantRequired` | Always use the default variant: contradicts P-08c and can sell the wrong size. Make the variant id mandatory now: breaks the storefront and every existing client for no benefit while products have one variant |
| A variant that isn't this product's (another product's, or another store's) | **Not found.** The variant is looked up inside the product the request names, which was itself loaded through the store-filtered repository. `POST /api/basket/items` answers 404. In pricing the line is unsellable, so checkout answers `ProductNotFound` | Trusting a variant id because it exists: a request could price product A with product B's cheaper variant. A distinct "wrong product" code: it tells a caller that the id exists elsewhere |
| Deactivating and deleting variants | `ProductVariant.IsActive`; **variants are never deleted** (order lines, reservations, the ledger and basket lines reference them). A deactivated variant can't be bought and its basket lines become unsellable. **The default variant is always active**, guarded by `Product.DeactivateVariant` and the check constraint `CK_ProductVariants_DefaultIsActive` | A delete with a cascade: history would lose its reference. Allowing an inactive default: every client that doesn't know about variants would lose the product |
| Product-keyed basket and stock routes | **Kept, and valid while unambiguous**: the product has one line in the basket, or one stock row. Otherwise they answer `422 VariantRequired`. **Variant-keyed routes** sit beside them: `PUT`/`DELETE api/basket/items/variants/{variantId}` and `api/admin/inventory/variants/{variantId}/movements\|adjustments\|threshold` | Replacing the product routes: breaks today's frontend for no present benefit. Acting on the default variant: silently edits a line or stock row the caller didn't mean |
| Where "one stock row" is counted | **Inside Inventory**: the number of `InventoryItems` for the product. The repository no longer joins Catalog's `IsDefault` flag | Reading Catalog's variants from Inventory: an extra module crossing for a fact Inventory already owns |
| Creating a second variant before options exist | `Product.AddVariant` is **internal**, visible only to the test projects. Merchants create variants in V2, with option values | A public method: a variant without option values contradicts P-08a. Waiting for V2 before testing: the multi-variant paths of basket, pricing, order and stock would ship unproven |
| The label snapshot | A nullable text in the store's default culture, like `ProductName`. It stays **null until options exist** (V2 composes it from option values) | Per-language snapshots: a separate decision; today's order lines are single-language too |

## Decision

The options marked "Chosen" above.

- **Domain.**
  - `OrderItem` gains `VariantId`, `VariantLabel` and `Sku`.
  - `Order.AddItem(productId, variantId, name, unitPrice, quantity, variantLabel, sku)` merges by variant.
  - `Product` gains `ImplicitVariant`, `FindVariant`, `CanSell(variant)`, `DeactivateVariant` and `ActivateVariant`.
  - `ProductVariant` gains `IsActive`.
  - `Basket.LineFor(productId)` becomes `LineForVariant(variantId)` plus `LinesFor(productId)`.
- **Contracts.**
  - `PricingLine` gains an optional `VariantId`.
  - `PricedLine` gains `VariantLabel`, `Sku` and `VariantRequired`.
  - `OrderLineInput` and `BasketItemRequest` gain an optional `variantId`.
  - `OrderItemDto` gains `VariantId`, `VariantLabel` and `Sku`.
  - `InventoryItemDto`, `StockLevelDto` and `StockMovementDto` gain `VariantId`.
  - The inventory list returns **one row per variant**. It shows the same rows as before while every product has one variant.
- **Schema** (migration `OrderLinesRecordVariant`, additive):
  - `ProductVariants.IsActive`, default true for existing rows, with `CK_ProductVariants_DefaultIsActive`.
  - `OrderItems.VariantId`, not null after the backfill, with a same-store foreign key to `ProductVariants` (restrict).
  - `OrderItems.VariantLabel` and `OrderItems.Sku`.
  - A unique index on (`OrderId`, `VariantId`), which replaces the plain `OrderId` index it covers.
  - An index on (`TenantId`, `VariantId`).
- **The P-08 answers** in the Context section bind V2 and V3.

## Consequences

- **Positive:**
  - An order records exactly what was bought, and an invoice can't merge two sizes or show a price that wasn't charged.
  - The variant chosen in the basket is the one priced, reserved, ordered, paid for and consumed from the basket.
  - A client can't price one product with another's variant, in its own store or another.
  - Every existing call behaves as before for today's products, and existing orders stay readable with their exact variant.
  - V2 can add options without touching orders, checkout or stock.
- **Negative / limits:**
  - Historical order lines will never show a SKU or variant label. That is the honest state of the data.
  - The product-keyed routes become conditional. A client that ignores `VariantRequired` can't act on a multi-variant product, which is the intended failure.
  - `AddVariant` is internal, so no merchant can yet create a variant. Multi-variant behaviour exists and is tested, but no store can reach it until V2.
  - The storefront still sends product ids only. V3 moves it to variant-keyed calls.
  - The low-stock notification and order emails still name the product only. Labels arrive with options.
  - The migration refuses to run on a database that already has a non-default variant. That can only happen if variants are created by hand outside the application, and it is the point: such a database needs a human-made mapping.

## Revisit when

- **V2 (options and admin):** done in [ADR-0040](0040-product-option-model.md) — `AddVariant` is public with option values and the limits above, the label snapshot is composed from options, the admin inventory screen uses the variant routes, and the low-stock notification names the variant.
- **V3 (storefront):** the selector, "From" pricing, disabled sold-out values and variant-keyed basket calls. Two presentation defaults from the proposal still need the owner's confirmation there: hiding *deactivated* variants, and keeping a product with no purchasable variant visible as "unavailable".
- A store needs order-line texts in more than one language.

## Verification

- **Domain:**
  - `OrderTests`: two variants make two lines at their own prices with their snapshots; the same variant merges; a variant under another product or at another price is refused; empty snapshots stay null; snapshots don't change after placement.
  - `ProductVariantTests`: the implicit variant, deactivation and the always-active default, foreign variants, and sellability.
  - `BasketTests`: two variants make two lines.
- **Application:**
  - `PricingServiceTests`: a named variant is priced with its SKU; a foreign, inactive or missing variant is refused.
  - `BasketHandlersTests`: separate lines, `VariantRequired`, 404 for foreign or inactive variants, product routes refusing ambiguity, and variant routes.
  - `CreateOrderHandlerTests`: two lines with their SKUs and reservations per variant; `VariantRequired`; an inactive variant; a basket checkout keeping the variant.
  - `BasketCheckoutTests`: consumption per variant.
  - `InventoryCommandsTests`: product routes refuse a multi-variant product; variant routes, audit records and validators.
- **Integration:**
  - `ProductVariantTests`, on real SQL Server:
    - basket → quote → order → payment → stock with two variants, and the SKU snapshot surviving a catalog change;
    - a single-variant product through every old contract;
    - `VariantRequired`, foreign and deactivated variants;
    - variant-keyed stock administration, list rows per variant, and audit;
    - the last unit of one variant under concurrency, leaving the other variant untouched;
    - the database constraints.
  - `TenantIsolationTests`: every new route for another store's variant answers 404, and another store's variant is rejected with the caller's own product in basket and checkout.
  - `MigrationRehearsalTests`: legacy order lines are backfilled with their exact default variant, with null SKU and label; the migration aborts, leaving the schema untouched, when a non-default variant exists.

## Migration

`OrderLinesRecordVariant` is hand-edited after generation:

- `IsActive` defaults to **true**; EF generated false, which would have hidden the whole catalog.
- The backfill and its three guards run inside the migration's transaction.
- The temporary default on `OrderItems.VariantId` is dropped, so an insert without a variant fails loudly.
- The unique index is created before the index it replaces is dropped.

`Down()` removes the columns and indexes, and restores the plain `OrderId` index. A rollback loses the variant references and snapshots recorded since, so the safe rollback is a restore ([Migrations.md](../06-DATABASE/Migrations.md)).
