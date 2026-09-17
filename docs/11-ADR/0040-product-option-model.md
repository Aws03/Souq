# ADR-0040: Product options and variants as combinations, managed by the merchant

- **Status:** Accepted, 2026-09-17. Implements V2 of [ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md). Its temporary storefront gate was **removed by [ADR-0041](0041-storefront-variant-selection.md)** when the shopper's selection (V3) was built.
- **Date:** 2026-09-17
- **Related modules:** Catalog; Inventory; Shopping; Ordering; Notifications
- **Related ADRs:** implements the option model decided in [ADR-0039](0039-product-variants-order-identity.md) (P-08a); keeps the default variant of [ADR-0025](0025-catalog-model.md) (D-21); opens stock through the port of [ADR-0026](0026-inventory-reservations.md); concurrency per [ADR-0013](0013-optimistic-concurrency.md); per-language texts per D-10; error codes per [ADR-0017](0017-error-contract.md); same-store keys per [ADR-0022](0022-tenancy-enforcement.md)

## Context

[ADR-0039](0039-product-variants-order-identity.md) recorded the owner's decision P-08 and built V1: orders, the basket, pricing, checkout and stock handle several variants of one product. It left `Product.AddVariant` internal, because a second variant without option values would contradict P-08a. Until now no merchant could create one.

P-08a set the shape: **structured named options, at most 3 per product, 20 values per option, 100 variants per product.** V2 turns that into a model the merchant can manage, without the storefront selection (V3).

## Problem

1. **How are option names and values stored** so they follow the store's languages, like every other catalog text?
2. **How is "no two variants share a combination" enforced** when two admins save at the same time, and when a value is created in the same save as the variant that uses it?
3. **What happens to existing variants** when an option is added, a value is removed, or an option is removed? Variants are never deleted, and every variant must carry one value of every option.
4. **How is a concurrent edit detected** when an option or variant change writes only child rows, and the product's rowversion is never checked?
5. **What happens to the product-level price and SKU** for a product with options, and to old clients that still send them?
6. **What does the storefront do** with a product that has several active variants, before it can offer a choice?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Names | Translation rows per culture (`ProductOptionTranslations`, `ProductOptionValueTranslations`), a lighter base than `CatalogTranslation` (name only, ≤ 50). The store default culture is required, because the order-line label is composed in it. Names are unique per product (options) and per option (values), **case-insensitively in each language**. **Control characters are rejected**, because labels reach order lines and emails (the store-name rule) | Fixed Arabic/English columns: D-10 rejected them. A JSON column: no uniqueness per culture, and a second text pattern in the catalog |
| Combination identity | Each value gets a stable `Key` (a GUID generated in the Domain). A variant's `CombinationKey` is its value keys sorted and joined, and a unique filtered index covers (`ProductId`, `CombinationKey`). A join table (`ProductVariantOptionValues`) references the value by a same-store key with **restrict** | Keys from database ids: they don't exist until the save, so a value and its variant couldn't be created together. Keys from positions: a reorder changes every key, and swapping two rows transiently violates the unique index |
| Adding an option to a product that has variants | The definition **names the value existing variants take** (`ExistingVariantsValue`), which the merchant chooses explicitly. The first option therefore converts the existing default variant in place: same id, stock, ledger and sales | Recreating variants: loses the history the order lines point to. Picking a value silently: an invented fact |
| Removing a value | Refused while **any** variant uses it, active or not (`OptionValueInUse`); the merchant deactivates the variant instead. The proposal said "active variants"; the stricter reading follows from variants never being deleted and each keeping a full combination | Clearing the value from inactive variants: leaves a variant with an incomplete combination, and the database refuses it anyway |
| Removing an option | Allowed only if every variant's combination stays unique afterwards (`OptionRemovalCollides`). Removing every option requires a single variant, which then becomes a simple product again | Deleting colliding variants: variants are never deleted |
| Updating the option set | One `PUT options` replaces the whole definition in order: ids keep existing options and values, entries without an id are new, and missing ones are removed. **Every check runs before any change**, so a refused request leaves the aggregate untouched | Granular endpoints per option and value: many more routes, and the invariant "each variant has one value per option" spans several requests |
| The limit of 100 variants | Counts **every** variant, including inactive ones: their rows exist and bound the admin matrix and the queries | Counting active variants only: the table could grow without bound through deactivation |
| Concurrency | Every structural edit calls `IProductRepository.GuardConcurrentEdit`, which marks the product row modified, so the save runs its rowversion check and a concurrent edit answers `409 ConcurrencyConflict`. The combination index and the value restrict remain the last line of defence | Relying on child-row indexes alone: a variant created during a concurrent "add option" would be saved without the new option's value. A domain revision counter: a persistence concern in the Domain |
| Product-level price and SKU (`PUT /api/products/{id}`) on a product with options | Accepted only if **unchanged**: an old client that sends back what it read can still rename the product. A real change answers `422 ProductHasVariants`. The admin form locks the fields and sends the read values | Ignoring the fields silently: a merchant believes a price changed. Refusing any PUT: breaks every existing client |
| Creating variants | `POST variants` takes a batch (the admin's "create missing combinations"). Each item names value ids and sets price, compare-at, SKU, opening stock, threshold and active flag. **The batch is created all-or-nothing**, and its stock rows are opened through `IVariantStockInitializer` in the same transaction. The server resolves every value id inside the product; the browser's proposed combinations are never trusted | Generating all combinations on the server: a merchant rarely sells the full matrix (up to 8,000 combinations) |
| The default variant | Movable (`PUT .../default`) to an **active** variant (`DefaultVariantMustBeActive`); the default can't be deactivated (`DefaultVariantCannotBeDeactivated`, and the V1 check constraint) | A fixed default: the original variant could never be retired |
| The storefront before V3 (**removed in [ADR-0041](0041-storefront-variant-selection.md)**) | **Temporary gate:** storefront projections (`CatalogQueries` lists, detail, related products, and `WishlistQueries`) show a product only while it has **exactly one active variant**, the one the storefront can buy by product id. Storefront stock sums active variants only. The admin page tells the merchant, and new variants default to inactive while the product is visible. **V3 removes the gate** | Showing it: "Add to cart" would fail with `VariantRequired`. Refusing to publish a product with several variants: a merchant would have to unpublish a selling product to prepare sizes. Deciding a V3 presentation question now: the owner hasn't (§10 of ProductVariants.md) |
| Where the label is composed | One Domain rule, `VariantLabels.Compose`: value names in option order, joined by `" / "`, each in the requested culture or else the first available. It is used by `Product.VariantLabel` for the order snapshot, pricing and the low-stock notification, and by the inventory projection | A second composition in SQL or the frontend: the rules would drift. (The admin page composes labels in the interface language with the same rule in `variantModel.js`, for display only) |
| The low-stock notification | The Notifications handler already loads the product for its name and now reads `Product.VariantLabel(variantId, culture)`, adding `variantId` and `variantLabel` to the data. Inventory still knows nothing about options, and the module-dependency ratchet is unchanged | Putting the label on the `StockBecameLow` event: Inventory would need Catalog's option model |

## Decision

The options marked "Chosen" above.

- **Domain (Catalog).**
  - New types:
    - `ProductOption`, `ProductOptionValue` (`Key`) and `ProductVariantOptionValue`;
    - `OptionTranslation`, with `ProductOptionTranslation` and `ProductOptionValueTranslation`;
    - `ProductOptionDefinition` and `ProductOptionValueDefinition`;
    - `OptionNames` and `VariantLabels`;
    - `InvalidProductVariantException`, which carries one stable code per rule.
  - `Product` gains:
    - `MaxOptions`, `MaxValuesPerOption` and `MaxVariants`;
    - `Options` and `HasOptions`;
    - `SetOptions`, `FindOptionValue` and `VariantLabel`;
    - a public `AddVariant(valueIds, price, compareAt, sku, isActive)`, `UpdateVariant` and `SetDefaultVariant`.
  - `SetPricing` refuses a real change on a product with options.
  - `ProductVariant` gains `OptionValues` and `CombinationKey`.
- **Application.**
  - `SetProductOptionsCommand`, `CreateProductVariantsCommand`, `UpdateProductVariantCommand`, `SetProductVariantStatusCommand` and `SetDefaultProductVariantCommand`, each with a validator and an audit record.
  - `IProductRepository.GuardConcurrentEdit`.
  - `AdminProductDto` gains `Options`, `Variants` (with per-variant stock) and `VariantLimits`.
  - `AdminProductListItemDto.VariantCount`.
  - `InventoryItemDto` gains `VariantLabel` and `VariantIsActive`.
  - `PricedLine.VariantLabel` is filled from the options.
- **API** (`catalog.manage`, under `api/admin/products/{id}`):
  - `PUT options`, `POST variants`;
  - `PUT variants/{variantId}`, `PUT variants/{variantId}/status`, `PUT variants/{variantId}/default`.
  - A variant id from another product answers 404.
- **Schema** (`ProductOptionsAndVariants`, additive, no data moved):
  - the five tables above;
  - `ProductVariants.CombinationKey varchar(110)`, with a unique filtered index on (`ProductId`, `CombinationKey`);
  - (`ProductVariantId`, `OptionValueId`) unique;
  - (`TenantId`, `OptionValueId`) → `ProductOptionValues` (`TenantId`, `Id`), restrict;
  - (`OwnerId`, `Culture`) unique on both translation tables.
- **Frontend.**
  - `/admin/products/:productId/variants`: the options editor, the variants table, and "create variants". Pure logic lives in `frontend/src/features/admin/products/variantModel.js`.
  - The inventory screen shows one row per variant, and the stock drawers use the variant routes.
  - The product form links to the page and locks pricing on a product with options.

## Consequences

- **Positive:**
  - A merchant can sell a product in sizes and colours: define options in Arabic and English, create the combinations they sell, and price, stock, activate, deactivate and default each variant.
  - Existing variants keep their ids, stock and history through every change, and an order line snapshots the label ("M / أحمر") at purchase.
  - Duplicate combinations, a variant with a missing value, a removed value that a variant uses, and cross-store value references are impossible even at the database level. Concurrent structural edits of one product are detected.
  - Single-variant products and historical orders are unchanged; the migration moves no rows.
- **Negative / limits:**
  - ~~Until V3, a product with more than one active variant is not shown in the storefront at all.~~ **Removed in [ADR-0041](0041-storefront-variant-selection.md):** the storefront shows it and the shopper chooses. New variants are created active, and the admin page no longer warns about hiding.
  - A value used by an inactive variant can never be removed, only renamed. That is the price of never deleting variants.
  - The `PUT options` body is the whole definition. A client that edits from a stale read may remove a value another admin just added; the concurrency guard catches a simultaneous save but not a stale form submitted later.
  - The order-line label is a snapshot in the store's default culture only, as before.
  - Variant images and a manual variant sort order are not built. Variants are listed in option-value order.
  - The dashboard's stock figures now count variants (V4 relabels them).

## Revisit when

- ~~**V3:** remove the storefront gate…~~ **Done in [ADR-0041](0041-storefront-variant-selection.md)**, which also answered the two presentation questions.
- A merchant needs to reorder variants manually, or attach a gallery image to a variant.
- A store needs more than 3 options, 20 values or 100 variants. That is a new owner decision, not a constant change.

## Verification

- **Domain:**
  - `ProductOptionTests`:
    - limits, names unique per language ignoring case, control characters;
    - conversion of the default variant, explicit values for existing variants;
    - complete and unique combinations, foreign values;
    - a used value (including one used by an inactive variant) can't be removed;
    - renaming and reordering don't change keys;
    - option removal and collisions;
    - atomic failure;
    - label composition.
  - `ProductVariantTests`: moving the default, updating a variant, and product-level pricing on a product with options.
- **Application:** `ProductVariantHandlersTests`:
  - resolution inside the store's product, and 404 for a foreign variant on every command;
  - the default-culture requirement;
  - store-wide SKU uniqueness;
  - stock opened per variant in the transaction;
  - the concurrency guard before saving;
  - validators and audit records.
- **Notifications:** `NotificationHandlersTests` covers the variant label in low-stock data.
- **Integration** (SQL Server):
  - `ProductOptionAdminTests`:
    - the full merchant workflow over HTTP, including stock and audit;
    - rule violations with no side effects;
    - the label snapshot surviving a rename;
    - the storefront gate;
    - database constraints (duplicate combination, deleting a used value);
    - concurrent requests;
    - a deterministic stale-save conflict, mutation-checked (it fails with the guard removed);
    - the low-stock notification.
  - `TenantIsolationTests`: the five new routes, and another store's option, value and variant ids used with the caller's own product, including a raw insert rejected by the same-store foreign key.
  - `MigrationRehearsalTests`: legacy products stay simple.
  - `AuthorizationBoundaryTests` covers the new routes automatically (401 and 403).
- **Frontend:**
  - `variantModel.test.js`, `ProductVariants.test.jsx` (including axe) and `notificationView.test.js`.
  - Browser journey `frontend/e2e/product-variants.spec.js` on a live stack, in English and in Arabic RTL, in dark mode and at phone width.

## Migration

`ProductOptionsAndVariants` is generated and additive. Every existing product has no options and one default variant, which is already the shape of a simple product, so no row changes. `Down()` drops the five tables and `CombinationKey`, and loses every option and variant combination recorded since. Variants created in V2 would then remain as rows without options, which V1's invariants don't expect, so the safe rollback is a restore ([Migrations.md](../06-DATABASE/Migrations.md)).
