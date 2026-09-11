# ADR-0025: Catalog model: per-language texts, default variant, lifecycle, and image gallery

- **Status:** Accepted (implemented in Phase 5), 2026-09-11.
- **Decisions implemented:**
  - D-10 (catalog localization);
  - D-21 (sellable unit: a default variant for every product);
  - the Phase 5 part of D-18 (a tenant-prefixed image gallery).
- **Builds on:** [ADR-0009](0009-ddd-usage.md), [ADR-0014](0014-money-precision.md), [ADR-0016](0016-upload-validation.md) and [ADR-0022](0022-tenancy-enforcement.md). It changes none of them.
- **Date:** 2026-09-11
- **Related modules:** Catalog; Inventory (the variant its stock hangs off)
- **Related ADRs:** builds on [ADR-0009](0009-ddd-usage.md), [ADR-0014](0014-money-precision.md), [ADR-0016](0016-upload-validation.md) and [ADR-0022](0022-tenancy-enforcement.md); its read projections follow [ADR-0008](0008-cqrs-strategy.md); stock moves from the product to the variant in [ADR-0026](0026-inventory-reservations.md); basket and order lines point at its variants in [ADR-0028](0028-basket-and-pricing-pipeline.md) and [ADR-0029](0029-orders-lifecycle.md)

## Context

Before Phase 5 the catalog was the single-store model:
- **Product:**
  - fixed `NameAr` / `NameEn` / `Description` columns;
  - one `ImageUrl`;
  - price and stock on the product;
  - an `IsActive` flag. "Delete" meant deactivate, and that hid the product from the admin list as well (Phase 0 C7).
- **Category:** a name, a slug and a parent. It had no display order, no visibility flag, and no guard against cycles.

Stores already choose their languages (Phase 4 settings), but catalog texts could not follow them.

## Problem

How do we model localized texts, the sellable unit, the product lifecycle and media so that the next phases build on them without painful migrations?
- Phase 6 needs stock per variant.
- Phases 8–10 need cart and order lines that can point at a variant.
- Phase 16 needs storefront URLs by slug.

The existing development data must also migrate without loss.

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Localized texts | Translation tables per aggregate (`ProductTranslations`, `CategoryTranslations`): one row per (entity, culture) with name, description and SEO title/description. Unique `(ParentId, Culture)`. | Columns per language: a migration per language, and a store cannot add one. A JSON column: searching names needs JSON functions in every query, and uniqueness per culture cannot be indexed. |
| Sellable unit | Every product has exactly one **default `ProductVariant`**, enforced by a filtered unique index. It holds the SKU, the price and the compare-at price; `Product.Price` is a shortcut to it. Stock stays on `Product` until Phase 6 moves it to an inventory item per variant. | Price and SKU on the product, variants later: Phases 6, 8 and 9 would then migrate carts, order lines and stock from product to variant. A full option matrix (size, colour) now: out of scope, and the model admits it later. |
| Lifecycle | `ProductStatus` Draft / Active / Archived. A product is never hard-deleted, because orders and reviews reference it: `DELETE` archives it. The admin lists every status; the storefront shows Active products in active categories. | The `IsActive` flag: it cannot tell "not yet published" from "retired", and it hid products from the admin (C7). |
| Slugs | Unique per store, for products and categories. The server suggests one from the Latin name and adds a numeric suffix on collision; an explicit slug that is taken → 409 `ProductSlugTaken`. | Globally unique slugs: they couple stores to each other, and two stores could not use the same URL. |
| Compare-at price | One amount column on the variant, in the price's currency, which must be higher than the price. The `onSale` filter means compare-at > price. | A separate offers table: campaign pricing belongs to Promotions, later. |
| Images | `ProductImages` child rows with a sort order, at most 10, the first one primary. Upload appends to the gallery; separate endpoints remove and reorder. Files stay under the tenant prefix. | One `ImageUrl` column: no gallery. Deleting the file inside the remove request: a storage call in the request path, for no user benefit; left to a cleanup job. |
| Category tree | A self-FK within the store. `Category.MoveTo` rejects cycles and trees deeper than 5 levels, using the ancestry the handler builds from one query of `(Id, ParentId)` pairs for the store. The tree also has `SortOrder` and `IsActive`, and a hidden category hides its products. | A recursive CTE per move: more SQL for a small table (a store has tens to hundreds of categories). A closure table: not justified at this size. |
| Rich description | Plain text, up to 4000 characters. | HTML with a sanitizer: needs a dependency decision. Deferred to the storefront phase (16), where it is rendered. |
| Attributes | Deferred. | — |

## Decision

- **Domain:**
  - `Product` (aggregate root) owns `ProductTranslation`, `ProductImage` and `ProductVariant`. Children are created only through the root, carry their `TenantId`, and use a shadow FK to the root.
  - `Category` owns `CategoryTranslation`.
  - `CatalogText` is an immutable value object, normalized and length-checked when assigned. `CatalogSlug` normalizes slugs.
  - `SetTexts` replaces the whole set, so a language that is no longer sent is removed.
  - The text in the store's default language is required. Order lines snapshot the name in that language.
- **Application:**
  - Create and update commands carry `translations`.
  - New commands: `ChangeProductStatus`, `RemoveProductImage`, `ReorderProductImages`.
  - New queries: `ListAdminProducts`, `GetAdminProduct`, `ListAdminCategories`, `GetProductBySlug`.
  - Store queries take the store's default culture for the top-level `name` and `description`, and return every translation, so the UI can switch languages without a new request.
- **Infrastructure:**
  - Projections read translations, the primary image and the default variant as correlated subqueries of one statement. An integration test counts the SQL commands and requires the same count for 2 and 10 products.
  - The repositories load the aggregate with its children (split query).
- **API:**
  - `GET /api/admin/products` (status, category, keyword over name, SKU and slug; sort; page).
  - `GET /api/admin/products/{id}`, `PUT …/status`, `DELETE …/images/{imageId}`, `PUT …/images/order`.
  - `GET /api/admin/categories`.
  - Public: `GET /api/products/by-slug/{slug}` and the `onSale` filter.
  - All admin endpoints use the `catalog.manage` permission, and every write is audited.

## Migration

`Phase5Catalog` was rewritten by hand to preserve data, because EF's generated order drops the old columns first:
1. Add the new columns and create the child tables.
2. Copy the data in SQL:
   - an Arabic translation from `NameAr` + `Description`;
   - an English one from `NameEn` where present and different;
   - a default variant from the price;
   - images only from real `/uploads/` or http(s) URLs, so the old placeholder values are dropped;
   - `Status` from `IsActive`;
   - slug `p-{Id}`;
   - each category's name as a translation in its store's default language.
3. Drop the old columns and create the indexes.

`Down` copies everything back. `MigrationRehearsalTests` applies it to a database shaped like Phase 4 with legacy rows, and reads the result through EF.

## Consequences

- **Positive:**
  - Stores localize their catalog in the languages they enable.
  - Phase 6 moves stock onto the variant without touching carts or orders again.
  - Archived products stay visible to the admin and on past orders.
  - Offers are real, via compare-at.
  - Product URLs can be stable slugs.
- **Costs:**
  - Every list reads translations through subqueries. They are indexed by the parent key, and query-count tests guard against N+1.
  - `PUT` replaces all translations, so a client must send every language it keeps. The admin form round-trips SEO fields it does not display.
  - An image removed from a gallery leaves its file in storage until a cleanup job exists.
- **Follow-ups:**
  - attributes and the variant option matrix;
  - a sanitized rich description (16);
  - storefront routes by slug and a sitemap (16);
  - an orphaned-file cleanup job;
  - cloud blob storage (D-18, Phase 23).
