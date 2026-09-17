# Catalog module

> **Code:** `src/Souq.Application/Features/Products`, `src/Souq.Application/Features/Categories`, `src/Souq.Domain/Entities/Product.cs`, `src/Souq.Domain/Entities/ProductParts.cs`, `src/Souq.Domain/Entities/Category.cs`, `src/Souq.Domain/Entities/CatalogTranslation.cs`, `src/Souq.Infrastructure/Persistence/Queries/CatalogQueries.cs` · **Decisions:** [ADR-0025](../../11-ADR/0025-catalog-model.md), [ADR-0016](../../11-ADR/0016-upload-validation.md), [ADR-0013](../../11-ADR/0013-optimistic-concurrency.md), [ADR-0008](../../11-ADR/0008-cqrs-strategy.md), [ADR-0026](../../11-ADR/0026-inventory-reservations.md) · **Change guide:** [ChangeGuide.md](ChangeGuide.md)

## Purpose

Catalog owns what a store sells and how it is presented: products with their per-language texts, their sellable variants (price, compare-at price, SKU) with the options that distinguish them, their media, and the category tree that organises them. It is its own module because product presentation changes at a different rate, under different rules, and by different people than stock levels (Inventory) or commercial records (Ordering). Categories are inside Catalog rather than beside it because they exist only to organise products ([Modules.md](../Modules.md)).

## Responsibilities

- The `Product` aggregate: texts per language, slug, status lifecycle, brand, video URL, an ordered image gallery, up to 3 options with their values (`ProductOption`, `ProductOptionValue`), and its variants (`ProductVariant`, each a combination of option values), exactly one of them the default that carries the product's list price.
- The `Category` tree: texts, slug, parent, display order, visibility flag, cycle and depth guards.
- Storefront reads: listing with search, category/price/on-sale filters, sorting and paging; detail by id or by slug; related products; the active category list.
- Admin reads: every status, search over name, SKU and slug, sorting, and the full edit model including all languages and image ids.
- Product media: accepting an upload, validating it by content, and storing it through `IFileStorage`.
- Declaring `IVariantStockInitializer` so that creating a product opens its stock without Catalog depending on Inventory.

## Not this module's job

| Concern | Owner |
|---|---|
| Stock on hand, reservations, ledger, low-stock thresholds | [Inventory](../Inventory/README.md) |
| The price and name a customer actually paid | Ordering (`OrderItem` snapshots both) |
| Discounts, coupons, campaign pricing (compare-at price is catalog data; a discount is not) | Promotions |
| The basket pricing pipeline (`IPricing`) | Shopping |
| Ratings and reviews | Reviews |
| Store languages, default culture, currency | Platform (`Tenant`, `StoreSettings`) |
| Where uploaded bytes actually live | Infrastructure adapter `LocalFileStorage` behind the shared port `IFileStorage` |
| Low-stock alerts and emails | Notifications |

## Business concepts

- **Product** — something the store sells. Never deleted, only archived, because orders and reviews point at it.
- **Default variant** — the variant that represents the product in lists and for clients that don't know variants. `Product.Price` and `Product.Sku` are shortcuts to it. Always active; a simple product has only this one.
- **Option / option value** — a named dimension (size, colour) and its values, per language. A **combination** is one value of every option; each variant is one combination, identified by `CombinationKey` ([ADR-0040](../../11-ADR/0040-product-option-model.md)).
- **Variant label** — the variant's value names in option order ("M / أحمر"), composed by `VariantLabels.Compose`.
- **Compare-at price** — the "was" price. When it is higher than the price, the product is on sale.
- **Status** — `Draft` (being prepared, hidden from customers), `Active` (visible and sellable), `Archived` (retired, hidden, restorable).
- **Slug** — the URL identifier of a product or a category, unique inside one store.
- **SKU** — the stock-keeping code of a variant, unique inside one store when set.
- **Translation** — name, description, SEO title and SEO description in one language.
- **Gallery** — up to ten ordered images; the first one is the primary image.
- **Category** — a node of a per-store tree at most five levels deep; it can be hidden.
- **Visible product** (storefront) — `Active` and sitting in an active category.

## Domain model

| Type | Kind | Path | Invariants it guards |
|---|---|---|---|
| `Product` | aggregate root | `src/Souq.Domain/Entities/Product.cs` | Never created `Archived`; slug normalised by `CatalogSlug` (2–`SlugMaxLength`, lower-case Latin, digits, single hyphens); category id > 0; texts normalised by `CatalogText`; brand ≤ `BrandMaxLength`; video URL ≤ `VideoUrlMaxLength`; at most `MaxImages` images; a reorder must list every image exactly once; exactly one default variant, created in the constructor and never deactivated (`DeactivateVariant` refuses it); a variant is found, sold and deactivated only inside its own product (`FindVariant`, `CanSell`, `ImplicitVariant` — the only active variant, or none when there are several); options and variants through `SetOptions`, `AddVariant`, `UpdateVariant`, `SetDefaultVariant` (BR-CAT-19 to BR-CAT-23: limits, unique names per language, complete and unique combinations, used values kept, a movable always-active default); `SetPricing` refuses a real change on a product with options |
| `ProductOption`, `ProductOptionValue`, `ProductVariantOptionValue`, `OptionTranslation` | entities in the Product aggregate | `src/Souq.Domain/Entities/ProductOptions.cs` | Created and changed only through `Product.SetOptions` and `Product.AddVariant`; names normalised by `OptionNames` (supported cultures, ≤ 50, no control characters); a value's `Key` never changes |
| `ProductVariant` | entity in the Product aggregate | `src/Souq.Domain/Entities/ProductParts.cs` | Price > 0; compare-at price in the same currency and strictly higher than the price; SKU trimmed, upper-cased, `^[A-Za-z0-9][A-Za-z0-9._-]*$`, ≤ `SkuMaxLength`; `IsActive`, `CombinationKey` and `OptionValues`, changed only through `Product`. Its constructor, `SetPricing`, `SetActive`, `SetDefault` and `SetOptionValues` are `internal`, so only `Product` may change pricing or activity. Never deleted: order lines, reservations, the ledger and basket lines reference it |
| `ProductImage` | entity in the Product aggregate | `src/Souq.Domain/Entities/ProductParts.cs` | URL present and ≤ `UrlMaxLength`; sort order assigned by the root |
| `ProductTranslation` | entity in the Product aggregate | `src/Souq.Domain/Entities/CatalogTranslation.cs` | One row per culture (unique index); created only through the root |
| `Category` | aggregate root | `src/Souq.Domain/Entities/Category.cs` | Slug 2–`SlugMaxLength`; sort order ≥ 0; `MoveTo` rejects a parent that is the category itself or one of its descendants, and any move that would push the tree past `MaxDepth` (5); a new category is active |
| `CategoryTranslation` | entity in the Category aggregate | `src/Souq.Domain/Entities/CatalogTranslation.cs` | As `ProductTranslation` |
| `CatalogTranslation` | abstract base, not mapped | `src/Souq.Domain/Entities/CatalogTranslation.cs` | `Replace` updates, adds and **removes** cultures so that a language no longer sent disappears; `NameIn` falls back to the first culture in ordinal order |
| `CatalogText` | value object | `src/Souq.Domain/ValueObjects/CatalogText.cs` | Name required and ≤ `NameMaxLength`; description ≤ `DescriptionMaxLength`; meta title ≤ `MetaTitleMaxLength`; meta description ≤ `MetaDescriptionMaxLength`; blanks become null; at least one culture, and every culture must be in `Tenant.SupportedCultures` (`ar`, `en`) |
| `CatalogSlug` | static domain helper | `src/Souq.Domain/Entities/CatalogTranslation.cs` | The one slug normalisation rule shared by products and categories |
| `ProductStatus` | enum | `src/Souq.Domain/Enums/ProductStatus.cs` | `Draft` = 0, `Active` = 1, `Archived` = 2 |
| `Money` | value object (shared kernel) | `src/Souq.Domain/ValueObjects/Money.cs` | Non-negative, valid currency, amount representable in the currency's minor units ([ADR-0014](../../11-ADR/0014-money-precision.md)) |
| `InvalidProductDataException` | domain exception | `src/Souq.Domain/Exceptions/DomainException.cs` | Code `InvalidProductData` → 422 |
| `InvalidCategoryException`, `InvalidCategoryParentException` | domain exceptions | `src/Souq.Domain/Exceptions/CatalogExceptions.cs` | Codes `InvalidCategory` and `InvalidParent` → 422 |

**Aggregate boundaries.** `Product` owns its translations, images, options (with their values and translations) and variants (with their option values) through a shadow foreign key `ProductId` with cascade delete (the root itself is never deleted). `Category` owns its translations. A product points at its category by id; the `Category` navigation exists for read projections only. All children are `ITenantOwned` and get their `TenantId` stamped by `TenantWriteGuardInterceptor`.

**Concurrency.** `Products` carries a shadow `RowVersion` (`HasRowVersion` in `ProductConfiguration`), so two admins editing the same product row conflict with `ConcurrencyConflict` (409). EF only checks that token when a `Products` column changes — slug, status, brand, video URL or category. An edit that touches only child tables (price and SKU through the product form, translations, images) issues no `Products` UPDATE and is therefore **last write wins**. **Option and variant edits are the exception** ([ADR-0040](../../11-ADR/0040-product-option-model.md)): their handlers call `IProductRepository.GuardConcurrentEdit`, which forces the `Products` UPDATE so the rowversion rejects a concurrent structural edit; [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md) records the same for variants. `Categories` has no concurrency token. The Arabic comment above `HasRowVersion` in `ProductConfiguration` still explains the token in terms of stock; stock left Catalog in Phase 6.

**Lifecycle.** `ChangeStatus` accepts any of the three statuses from any state: publish a draft, unpublish an active product back to `Draft`, archive, or restore an archived product straight to `Draft` or `Active`. There is no transition table — the only rule is that a *new* product may not be `Archived`. `DELETE /api/products/{id}` archives (`Archive()`); nothing in the module hard-deletes a product.

## Use cases

| Use case | Command or query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| Browse and search the catalog | `GetProductsQuery` | `GetProductsHandler` | anonymous | `GET /api/products` |
| Product detail by id | `GetProductByIdQuery` | `GetProductByIdHandler` | anonymous | `GET /api/products/{id}` |
| Product detail by slug | `GetProductBySlugQuery` | `GetProductByIdHandler` | anonymous | `GET /api/products/by-slug/{slug}` |
| Related products | `GetRelatedProductsQuery` | `GetRelatedProductsHandler` | anonymous | `GET /api/products/{id}/related` |
| Create a product and open its stock | `CreateProductCommand` | `CreateProductHandler` | `catalog.manage` | `POST /api/products` |
| Update a product | `UpdateProductCommand` | `UpdateProductHandler` | `catalog.manage` | `PUT /api/products/{id}` |
| Archive a product | `DeleteProductCommand` | `DeleteProductHandler` | `catalog.manage` | `DELETE /api/products/{id}` |
| Publish, unpublish, archive, restore | `ChangeProductStatusCommand` | `ChangeProductStatusHandler` | `catalog.manage` | `PUT /api/admin/products/{id}/status` |
| Add an image to the gallery | `UploadProductImageCommand` | `UploadProductImageHandler` | `catalog.manage` | `POST /api/products/{id}/image` |
| Set the product video | `UploadProductVideoCommand` | `UploadProductVideoHandler` | `catalog.manage` | `POST /api/products/{id}/video` |
| Remove an image | `RemoveProductImageCommand` | `RemoveProductImageHandler` | `catalog.manage` | `DELETE /api/admin/products/{id}/images/{imageId}` |
| Reorder the gallery | `ReorderProductImagesCommand` | `ReorderProductImagesHandler` | `catalog.manage` | `PUT /api/admin/products/{id}/images/order` |
| Admin product list (all statuses) | `ListAdminProductsQuery` | `ListAdminProductsHandler` | `catalog.manage` | `GET /api/admin/products` |
| Admin edit model (with options, variants, per-variant stock and the published limits) | `GetAdminProductQuery` | `GetAdminProductHandler` | `catalog.manage` | `GET /api/admin/products/{id}` |
| Define options and values (whole definition) | `SetProductOptionsCommand` | `SetProductOptionsHandler` | `catalog.manage` | `PUT /api/admin/products/{id}/options` |
| Create variants (a batch, stock opened) | `CreateProductVariantsCommand` | `CreateProductVariantsHandler` | `catalog.manage` | `POST /api/admin/products/{id}/variants` |
| Update a variant's price, compare-at and SKU | `UpdateProductVariantCommand` | `UpdateProductVariantHandler` | `catalog.manage` | `PUT /api/admin/products/{id}/variants/{variantId}` |
| Activate or deactivate a variant | `SetProductVariantStatusCommand` | `SetProductVariantStatusHandler` | `catalog.manage` | `PUT /api/admin/products/{id}/variants/{variantId}/status` |
| Make a variant the default | `SetDefaultProductVariantCommand` | `SetDefaultProductVariantHandler` | `catalog.manage` | `PUT /api/admin/products/{id}/variants/{variantId}/default` |
| Storefront category list | `GetCategoriesQuery` | `GetCategoriesHandler` | anonymous | `GET /api/categories` |
| Admin category list (includes hidden) | `ListAdminCategoriesQuery` | `GetCategoriesHandler` | `catalog.manage` | `GET /api/admin/categories` |
| Create a category | `CreateCategoryCommand` | `CreateCategoryHandler` | `catalog.manage` | `POST /api/categories` |
| Update, move, show or hide a category | `UpdateCategoryCommand` | `UpdateCategoryHandler` | `catalog.manage` | `PUT /api/categories/{id}` |
| Delete an empty category | `DeleteCategoryCommand` | `DeleteCategoryHandler` | `catalog.manage` | `DELETE /api/categories/{id}` |

Every command implements `IAuditable`; `AuditBehavior` stages an audit row before the handler runs. Validators (`CreateProductValidator`, `UpdateProductValidator`, `DeleteProductValidator`, `ChangeProductStatusValidator`, `ReorderProductImagesValidator`, `CreateCategoryValidator`, `UpdateCategoryValidator`, `DeleteCategoryValidator`, `GetProductsQueryValidator`, `GetRelatedProductsQueryValidator`, `GetProductBySlugQueryValidator`, `ListAdminProductsQueryValidator`) run in `ValidationBehavior` before that.

**Frontend.** Storefront: the home page (`frontend/src/pages/Store.jsx` rendering `frontend/src/pages/Storefront.jsx`), the searchable catalogue (`frontend/src/components/catalog/Catalog.jsx` over `frontend/src/hooks/useCatalog.js`), `/offers` (`frontend/src/pages/Offers.jsx`), and `/products/:handle` (`frontend/src/pages/ProductDetail.jsx`, loading by slug through `api.getProductBySlug`), with cards from `frontend/src/components/product/ProductCard.jsx`. Admin: `/admin/products` (`frontend/src/pages/admin/Products.jsx`, `frontend/src/pages/admin/ProductFormDrawer.jsx`) and `/admin/categories` (`frontend/src/pages/admin/Categories.jsx`, `frontend/src/pages/admin/CategoryFormDrawer.jsx`); archiving, deleting a category and removing an image confirm in `ConfirmDialog`. Pure rules: `frontend/src/features/catalog/` and `frontend/src/features/admin/products/`, `frontend/src/features/admin/categories/`.

## Public contracts

- **`IVariantStockInitializer`** (`src/Souq.Application/Features/Products/Contracts/IVariantStockInitializer.cs`) — the only type in Catalog's contracts folder, and the reason Catalog can open stock without knowing Inventory. Catalog *declares* the port ("when a sellable variant is created, open stock for it with an initial quantity and a threshold"); Inventory *implements* it as `VariantStockInitializer`; `src/Souq.Application/DependencyInjection.cs` wires the two. The dependency arrow therefore still points Inventory → Catalog, and no cycle exists ([ADR-0026](../../11-ADR/0026-inventory-reservations.md)). `CreateProductHandler` calls it after the first save (so the product and variant ids exist) and inside its own transaction, so a product without stock cannot be committed.
- **`ICatalogQueries`** (`src/Souq.Application/Features/Products/Queries/ICatalogQueries.cs`) — the module's read port, implemented by `CatalogQueries`. It is Catalog's own port: no other feature folder references it.
- Catalog offers **no read contract to other modules**. Shopping, Notifications and Wishlist read products through the Domain port `IProductRepository` and the `Product` aggregate instead; Inventory, Reviews, Shopping and Platform read the catalog tables directly in their Infrastructure projections. See [Dependencies](#dependencies).
- *ISellableItems* — a checkout snapshot contract named in [ModuleBoundaries.md](../../02-ARCHITECTURE/ModuleBoundaries.md) and [Architecture.md](../../02-ARCHITECTURE/Architecture.md). It does not exist in the code and is not in the roadmap. FUTURE.

## Dependencies

**Uses**

- Platform: `ITenantContext.RequireTenant()` for `TenantInfo.DefaultCulture` (which language the DTO's `Name`/`Description` carry, and which language is mandatory) and `TenantInfo.Currency` (the currency of a new product's price). `CatalogText` reads `Tenant.SupportedCultures` directly in the Domain layer.
- Shared kernel: `Money`, `IUnitOfWork`, `IFileStorage`, `MediaFileInspector`, `Result`, `Error`, `PageRequest`, `PaginatedList`.
- Inventory, only through Catalog's own inverted port `IVariantStockInitializer`.

**Boundary leaks (read side, Infrastructure)** — none of these is caught by a test, because the architecture tests scan Application namespaces only:

| Leak | Where | What it reads | Why it is there |
|---|---|---|---|
| Catalog reads Inventory data | `CatalogQueries` | `InventoryItems` (`OnHand − Reserved`) for `ProductDto.StockQuantity`, `AdminProductListItemDto.Available` and `LowStockThreshold`, the `StockAsc` admin sort, and `AdminProductDto.OnHand`/`Reserved` | One SQL statement instead of a second round trip per list; [ADR-0026](../../11-ADR/0026-inventory-reservations.md) accepted it ("catalog and inventory projections read availability from `InventoryItems`") |
| Catalog reads Ordering data | `CatalogQueries.BestSellingFirst` | `Orders` + `OrderItems`, summing quantities of `Delivered` orders, for `ProductSortBy.BestSelling` and for ordering related products | Inherited from the single-store code. [Modules.md](../Modules.md) lists "reading orders" as forbidden for Catalog and says it would move behind an Ordering contract in Phase 9; Phase 9 shipped without moving it |

**Used by**

| Module | How |
|---|---|
| Inventory | Implements `IVariantStockInitializer`; `InventoryQueries` joins `Products`, `ProductVariants`, translations, images and categories for the admin screens |
| Shopping | `PricingService` loads `Product` aggregates via `IProductRepository.GetManyAsync` (name, translations, primary image) and prices the line's variant through `Product.FindVariant`, `Product.ImplicitVariant` and `Product.CanSell`; `AddBasketItemHandler` loads a product to resolve the requested or implicit variant with the same methods; the wishlist handlers in `src/Souq.Application/Features/Wishlist/WishlistUseCases.cs` check `IsSellable`; `WishlistQueries` reads `Products` and `InventoryItems` |
| Ordering | Indirectly, through Shopping's `IPricing`; `OrderItem` rows carry tenant-scoped FKs to `Products` and `ProductVariants` (`OrderItemConfiguration`), which is one reason variants are deactivated and never deleted |
| Notifications | `StockBecameLowHandler` loads the `Product` aggregate through `IProductRepository` just to render the product name |
| Reviews | `ReviewQueries` joins `Products` for the moderation list; `Reviews` has an FK to `Products` |
| Platform | `PlatformQueries` counts products across stores with the tenant filter ignored |

Because `OrderItems`, `Reviews`, `WishlistItems`, `Baskets` lines, `InventoryItems` and `StockMovements` all hold `Restrict` foreign keys to `Products`, the database itself enforces "a product is archived, never deleted".

**Enforced vs convention**

- *Enforced* by `ModuleAndContractRuleTests`: Catalog (`Products` + `Categories`) may reference no other feature folder at all, and only Inventory may reference Catalog — through `Features.Products.Contracts`. Cycles fail the build. `DependencyRuleTests` enforces the layer rules (Domain references nothing, Application knows no technology, controllers stay thin).
- *Convention only*: everything that goes through `Souq.Domain.Interfaces`/`Souq.Domain.Entities` (Shopping, Notifications and Wishlist using `IProductRepository` and `Product`) and everything in Infrastructure (cross-module table reads in the query services).

## Data ownership

| Table | EF configuration | Tenant-owned | Concurrency token | Indexes that encode business rules |
|---|---|---|---|---|
| `Products` | `ProductConfiguration` | yes | `RowVersion` | unique `(TenantId, Slug)`; `(TenantId, Status, CategoryId)`; composite FK `(TenantId, CategoryId)` → `Categories (TenantId, Id)`, `Restrict` |
| `ProductVariants` | `ProductVariantConfiguration` | yes | none | unique `(TenantId, Sku)` filtered `[Sku] IS NOT NULL`; unique `IX_ProductVariants_ProductId_Default` on `ProductId` filtered `[IsDefault] = 1` (exactly one default variant); check `CK_ProductVariants_DefaultIsActive` (the default is active); alternate key `(TenantId, Id)` for tenant-scoped references from Inventory, baskets and orders; `Price`/`Currency` and `CompareAtPrice` as `decimal(19,4)` (`PersistenceConventions.MoneyColumnType`) |
| `ProductOptions`, `ProductOptionTranslations`, `ProductOptionValues`, `ProductOptionValueTranslations`, `ProductVariantOptionValues` | `ProductOptionConfiguration` and its siblings in `ProductConfiguration.cs` | yes | none (guarded through the `Products` row) | one translation per owner and culture; `ProductOptionValues` alternate key `(TenantId, Id)`; `ProductVariantOptionValues` unique `(ProductVariantId, OptionValueId)` and a same-store FK to the value, **restrict** (a used value can't be deleted); `ProductVariants.CombinationKey` unique per product where not null |
| `ProductImages` | `ProductImageConfiguration` | yes | none | `(ProductId, SortOrder)`, not unique. The ten-image ceiling lives only in the Domain |
| `ProductTranslations` | `ProductTranslationConfiguration` via `CatalogTranslationMapping` | yes | none | unique `(ProductId, Culture)` |
| `Categories` | `CategoryConfiguration` | yes | none | unique `(TenantId, Slug)`; `(TenantId, ParentId, SortOrder)`; self FK on `ParentId`, `Restrict` |
| `CategoryTranslations` | `CategoryTranslationConfiguration` via `CatalogTranslationMapping` | yes | none | unique `(CategoryId, Culture)` |

Uniqueness is per store, not per platform: two stores may use the same slug and the same SKU (`TenantIsolationTests`). The product → category FK is composite, so the database itself refuses a product pointing at another store's category. The category → parent FK is **single-column**, so a cross-store parent is prevented only by `CreateCategoryHandler`/`UpdateCategoryHandler` reading the tenant-filtered `ICategoryRepository.ListLinksAsync` and answering `ParentNotFound`.

Data this module reads but does not own: `InventoryItems` (Inventory) and `Orders`/`OrderItems` (Ordering), both directly in `CatalogQueries`.

Migrations: `20260911130701_Phase5Catalog` (hand-ordered so that the data copy runs before the old columns are dropped) and `20260911141732_Phase6Inventory` (which removed the stock columns from `Products`). See [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md).

## API

All routes are tenant-resolved from the Host header. No catalog endpoint is behind a module flag: `StoreModules` has only `promotions`, `reviews` and `wishlist` — a store without a catalog is not a store.

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/products` | anonymous | — | Listing, search, filters, sort, paging (default page size 12, maximum `PagingRules.MaxPageSize`) |
| GET | `/api/products/{id}` | anonymous | — | Visible product detail |
| GET | `/api/products/by-slug/{slug}` | anonymous | — | Visible product detail by slug (slug is lower-cased and trimmed in the handler) |
| GET | `/api/products/{id}/related` | anonymous | — | Related products (`count` 1–24, default 6) |
| POST | `/api/products` | `catalog.manage` | — | Create; 201 with `{ id }` and a Location header pointing at `GET /api/products/{id}` |
| PUT | `/api/products/{id}` | `catalog.manage` | — | Update; 204. The route id overrides the body |
| DELETE | `/api/products/{id}` | `catalog.manage` | — | Archive; 204 |
| POST | `/api/products/{id}/image` | `catalog.manage` | — | multipart `file`; `RequestSizeLimit` 6 MB; 200 `{ id, imageUrl, sortOrder }` |
| POST | `/api/products/{id}/video` | `catalog.manage` | — | multipart `file`; `RequestSizeLimit` 55 MB; 200 `{ videoUrl }` |
| GET | `/api/admin/products` | `catalog.manage` | — | All statuses; `keyword`, `status`, `categoryId`, `sortBy`, paging (default 20) |
| GET | `/api/admin/products/{id}` | `catalog.manage` | — | Full edit model |
| PUT | `/api/admin/products/{id}/status` | `catalog.manage` | — | `{ "status": "Active" \| "Draft" \| "Archived" }` |
| DELETE | `/api/admin/products/{id}/images/{imageId}` | `catalog.manage` | — | Remove one image |
| PUT | `/api/admin/products/{id}/images/order` | `catalog.manage` | — | `{ "imageIds": [...] }`, the complete gallery in its new order |
| PUT | `/api/admin/products/{id}/options` | `catalog.manage` | — | 204; `{ "options": [{ "id"?, "names": { "ar", "en" }, "values": [{ "id"?, "names" }], "existingVariantsValue"? }] }` — the whole definition in order; 422 with a rule code |
| POST | `/api/admin/products/{id}/variants` | `catalog.manage` | — | 200 `{ ids }`; `{ "variants": [{ "optionValueIds", "price", "compareAtPrice"?, "sku"?, "initialStock"?, "lowStockThreshold"?, "isActive"? }] }` |
| PUT | `/api/admin/products/{id}/variants/{variantId}` | `catalog.manage` | — | 204; `{ "price", "compareAtPrice", "sku" }`; another product's variant → 404 |
| PUT | `/api/admin/products/{id}/variants/{variantId}/status` | `catalog.manage` | — | 204; `{ "isActive": bool }`; the default can't be deactivated |
| PUT | `/api/admin/products/{id}/variants/{variantId}/default` | `catalog.manage` | — | 204; the variant must be active |
| GET | `/api/admin/categories` | `catalog.manage` | — | Every category, including hidden ones |
| GET | `/api/categories` | anonymous | — | Active categories, ordered by `SortOrder` then id |
| POST | `/api/categories` | `catalog.manage` | — | 201 `{ id }` |
| PUT | `/api/categories/{id}` | `catalog.manage` | — | 204; the route id overrides the body; replaces the whole category, so the UI resends unchanged fields (`activationPayload` in `frontend/src/features/admin/categories/categoryForm.js`) |
| DELETE | `/api/categories/{id}` | `catalog.manage` | — | 204 |

**Storefront reads vs admin reads.** They are different projections on purpose ([ADR-0008](../../11-ADR/0008-cqrs-strategy.md)). The storefront path starts from `CatalogQueries.VisibleProducts()` (`Status == Active && Category.IsActive`, and until V3 exactly one active variant — BR-CAT-24) and returns `ProductDto`: the price of the default variant, available stock, the primary image, every translation plus `Name`/`Description` in the store's default culture, and — on detail only — the full ordered image list. The admin path starts from every row in `Products` and returns `AdminProductListItemDto`/`AdminProductDto`, which add the status, the SKU, `OnHand`/`Reserved`/`Available` and the low-stock threshold. Both use `QueryableExtensions.ToPageAsync`, whose signature forces an explicit projection and an already-ordered query; every sort ends with the id as a tie-breaker so pages never duplicate or drop a row (`QueryServiceTests`).

| Sort | Storefront (`ProductSortBy`) | Admin (`AdminProductSortBy`) |
|---|---|---|
| Newest | `Newest` — id descending | `Newest` — `CreatedAt` then id, descending |
| Price | `PriceAsc`, `PriceDesc` — default variant price | `PriceAsc`, `PriceDesc` |
| Name | — | `NameAsc` — the name in the store's default culture, else the first culture |
| Stock | — | `StockAsc` — available (`OnHand − Reserved`) ascending |
| Popularity | `BestSelling` — summed quantities over `Delivered` orders | — |

Search differs too: the storefront matches the keyword against translation names **and** descriptions in any language; the admin matches slug, translation names and SKU (upper-cased). Both are substring matches with no full-text index and no relevance ranking; the code comment in `CatalogQueries` states the translation matches become `CHARINDEX` rather than a `LIKE` pattern (not verified against generated SQL).

## Security and permissions

- One permission for the whole module: `catalog.manage` (`Permissions.Catalog.Manage`), granted to `TenantAdmin` and `TenantStaff` by `RolePermissions`. Storefront reads are `[AllowAnonymous]` and only ever return visible products.
- **Uploads** ([ADR-0016](../../11-ADR/0016-upload-validation.md)): the client's filename and content type are ignored. `MediaFileInspector.DetectAsync` reads the magic bytes and accepts JPEG, PNG, GIF and WebP for images and MP4 or WebM for video; `MediaCategory.Icon` (ICO) is deliberately a separate category so a favicon cannot be uploaded as a product image. Size ceilings are `MediaFileInspector.MaxImageBytes` (5 MB) and `MaxVideoBytes` (50 MB) in the use case, with a larger HTTP ceiling in the controller. `LocalFileStorage` writes `{RootPath}/tenants/{tenantId}/{folder}/{guid}{ext}`, validating the folder and extension defensively, and returns `/uploads/tenants/{tenantId}/{folder}/{guid}{ext}`. `Program.cs` serves `/uploads` with an allowlisted content-type map, `ServeUnknownFileTypes = false`, `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none'; sandbox`, and `TenantResolutionMiddleware` answers 404 for `/uploads/tenants/{other}/…` on the wrong host.
- **`VideoUrl` is not validated** beyond trimming and length: `CreateProductCommand`/`UpdateProductCommand` accept any string, so an admin calling the API directly can store an arbitrary URL. The admin UI only ever round-trips an uploaded URL (`buildProductPayload`), and the storefront renders it as the `src` of a `<video>` element (`frontend/src/components/product/ProductZoom.jsx`).
- **Audit** ([ADR-0017](../../11-ADR/0017-error-contract.md) for codes, D-17 for auditing): `catalog.product.created` (metadata `sku`, `status`, `price`), `catalog.product.updated`, `catalog.product.archived`, `catalog.product.status-changed`, `catalog.product.image-added`, `catalog.product.image-removed`, `catalog.product.images-reordered`, `catalog.product.video-uploaded`, `catalog.category.created`, `catalog.category.updated`, `catalog.category.deleted`. The create-product entry targets the slug the admin typed, so it is null when the server suggests the slug — the new product id never reaches the audit row.

## Tenant behaviour

- Every table is `ITenantOwned`: the named EF query filter in `AppDbContext` scopes reads, `TenantWriteGuardInterceptor` stamps and guards writes, and an id from another store simply does not exist (404, or `CategoryNotFound` for a foreign category id).
- Uniqueness, media prefixes, categories and languages are all per store.
- A new product's price uses `TenantInfo.Currency`; an update reuses the currency already on the product (`product.Price.Currency`), so a product keeps the currency it was created with.
- The text in the store's default culture is mandatory (`CatalogTexts.HasCulture` → `DefaultTranslationRequired`); every translation is returned to the client so the UI can switch languages without another request.
- Languages are validated against the platform-wide `Tenant.SupportedCultures` (`ar`, `en`), **not** against the store's `EnabledCultures`: a store that enabled Arabic only can still save English catalog text.
- Availability: the public endpoints are refused with 503 `StoreUnavailable` unless the store is `Active`; `catalog.manage` endpoints also answer while the store is `Provisioning` (`TenantAvailabilityMiddleware.IsOpen`).
- `DbSeeder` seeds three categories and eight products (with opening stock) for the default store when it has no categories yet.

## Events and background work

Catalog raises no domain events, enqueues no outbox messages and runs no hosted service. Opening stock at creation is a direct in-process call through `IVariantStockInitializer` rather than an event, because there is exactly one consumer ([ADR-0026](../../11-ADR/0026-inventory-reservations.md)). Cleaning up orphaned media files is a DEFERRED background job.

## External integrations

Only file storage, through `IFileStorage` → `LocalFileStorage` (local disk; `Storage:Local:RootPath`, else `wwwroot/uploads`, validated at startup in `AddStorage`). Cloud blob storage is PLANNED (D-18, Phase 23 in [ProductRoadmap.md](../../12-ROADMAP/ProductRoadmap.md)).

## Tests

| Layer | Classes | What they pin |
|---|---|---|
| Domain | `ProductTests`, `CategoryTests` (both in `tests/Souq.Domain.Tests/ProductTests.cs`) | Default variant on creation, no product born archived, texts replaced as a set, slug rejection, compare-at and SKU rules, the status lifecycle, gallery limits and reordering, category cycles and depth |
| Domain | `ProductOptionTests`, `ProductVariantTests` | Option and value limits, names unique per language, control characters, conversion of the default variant, complete and unique combinations, used values kept, option removal, reorder stability, atomic failure, labels; implicit variant, deactivation, the movable default, per-variant pricing, product-level pricing on a product with options |
| Domain | `DomainExceptionCodeTests` | The stable codes `InvalidProductData`, `InvalidCategory`, `InvalidParent` |
| Application | `CreateProductHandlerTests`, `UpdateProductHandlerTests`, `ProductLifecycleHandlerTests`, `GetProductByIdHandlerTests` (all in `tests/Souq.Application.Tests/Products/ProductHandlersTests.cs`) | Foreign category rejected before any save, default-language text required, store currency and slug suggestion, suffixing a suggested slug vs rejecting an explicit duplicate, SKU conflict, "creates the product and opens its stock in one transaction", "a failed stock initialisation fails the whole creation", archive-not-delete, restore |
| Application | `ProductVariantHandlersTests` | Options and variants resolved inside the store's product, 404 for another product's variant, default-language names, store-wide SKU, stock opened per variant in the transaction, the concurrency guard, validators, audit records, product-level pricing refused |
| Application | `UploadProductMediaHandlerTests`, `MediaFileInspectorTests` | Extension derived from content, disguised HTML rejected and never stored, video rejected on the image endpoint and vice versa, size limits, stream rewound before storage |
| Application | `GetProductsHandlerTests`, `GetRelatedProductsHandlerTests`, `CreateCategoryHandlerTests`, `UpdateCategoryHandlerTests`, `DeleteCategoryHandlerTests` | Filters, sort, page and culture passed to the read port; category slug, parent, depth, and the two delete guards |
| Integration | `CatalogTests` | Admin sees every status while the storefront sees only published products, translations round-trip, slug lookup and normalisation, compare-at puts a product on sale, cycle and depth rejection, a hidden category hides its products, gallery ordering and removal, **and a fixed SQL command count for 2 and for 10 products** |
| Integration | `ProductOptionAdminTests`, `ProductVariantTests` | The merchant workflow over HTTP with stock and audit, rule violations without side effects, the label snapshot, the storefront gate, database constraints, concurrent and stale structural edits, the low-stock label; V1's multi-variant basket, checkout and stock paths |
| Integration | `QueryServiceTests`, `UploadSecurityTests`, `LocalFileStorageTests`, `TenantIsolationTests`, `AuthorizationMatrixTests`, `MigrationRehearsalTests` | Paging and deterministic ordering, page-size limits, a deactivated product missing from list, detail and related; disguised uploads rejected and served media headers; tenant-prefixed storage keys; per-store slug and SKU uniqueness, cross-store image ids, uploads not served on another store's host; role matrix; the Phase 5 data copy |
| Architecture | `ModuleAndContractRuleTests`, `DependencyRuleTests` | Contract references and layering |
| Frontend | `catalogText.test.js`, `productPayload.test.js`, `productQuery.test.js`, `categoryForm.test.js`, `variantModel.test.js`, `ProductVariants.test.jsx` | Language fallback and form mapping, the create/edit payload (including "never send stock on edit"), admin query keys, the category tree helpers |

**Not covered today:** the `BestSelling` sort (no test references it), concurrent gallery uploads past ten images, concurrent price edits, the exact behaviour when a product sits in an active child of a hidden category, and `VideoUrl` content.

## Failure modes

| Situation | Code | HTTP | How it is handled |
|---|---|---|---|
| Malformed input (page size, lengths, missing fields, `Archived` on create) | `ValidationFailed` | 400 | `ValidationBehavior` |
| Category missing or belonging to another store | `CategoryNotFound` | 400 | Handler, before any write |
| No text in the store's default language | `DefaultTranslationRequired` | 400 | Handler |
| Unsupported culture, bad slug or SKU format, compare-at ≤ price, price ≤ 0, more than ten images, an incomplete reorder list | `InvalidProductData` | 422 | Domain exception, translated centrally by `GlobalExceptionHandler` |
| Price with more decimals than the currency allows | `InvalidMoney` | 422 | `Money` |
| Explicit slug already used | `ProductSlugTaken` | 409 | Handler |
| SKU already used | `SkuTaken` | 409 | Handler |
| Two requests race past those checks | `DuplicateValue` | 409 | Unique index → `UniqueConstraintViolationException` |
| Concurrent edit of the same `Products` row | `ConcurrencyConflict` | 409 | `RowVersion` → `ConcurrencyConflictException` |
| Product, image or category not found (or not visible, or another store's) | `NotFound` | 404 | `Error.NotFound` |
| No file in a multipart upload | `FileRequired` | 400 | Controller |
| File larger than the use-case limit | `FileTooLarge` | 400 | Handler (a body above the controller's `RequestSizeLimit` is rejected earlier by the server; that status is not pinned by a test) |
| Content that is not an allowed image or video | `UnsupportedMediaType` | 400 | `MediaFileInspector` — nothing is written to storage |
| Category slug taken | `SlugTaken` | 409 | Handler |
| Parent category missing | `ParentNotFound` | 400 | Handler |
| Move under itself or a descendant, or past five levels | `InvalidParent` | 422 | `Category.MoveTo` |
| Delete a category that still has products | `CategoryInUse` | 409 | Handler |
| Delete a category that still has children | `CategoryHasChildren` | 409 | Handler |
| Opening stock fails during creation | the Inventory error | 422/409/500 | The transaction rolls back: no product, no stock |
| Store suspended or archived (public read) | `StoreUnavailable` | 503 | `TenantAvailabilityMiddleware` |

## Common change scenarios

Add a field to products · add attributes or real variant options · change the slug or SKU rules · add a category rule · change what "visible in the storefront" means · add a storefront sort or filter · add image processing or move to cloud storage · add or enforce a language. Step-by-step in [ChangeGuide.md](ChangeGuide.md).

## Known limitations

1. **Shoppers can't choose a variant yet, so a product with more than one active variant is not shown in the storefront** (BR-CAT-24). Merchants define options and variants since V2 ([ADR-0040](../../11-ADR/0040-product-option-model.md)); the storefront selection is V3 of [ProductVariants.md](ProductVariants.md), which removes the gate.
2. ~~"Visible" means two different things.~~ **Resolved (R-07):** `Product.IsSellable` requires an active product in an active category, and pricing, the basket and the wishlist all use it (`Product.CanSell` for a variant), so a product in a hidden category can't be bought by id. Pinned by `PricingServiceTests` and `BasketHandlersTests`.
3. **Hiding a category hides only its direct products.** Products in an *active child* of a hidden category stay visible, and that child is still returned by `GET /api/categories` (the handler filters by `IsActive` alone, with no ancestor walk).
4. **Concurrency guards the `Products` row only** (see Domain model). Product-form price, SKU, texts and images are last write wins; option and variant edits are guarded. `PUT options` replaces the whole definition, so a form submitted from a stale read can remove a value another admin added in between.
5. **The store's enabled languages are not enforced** in catalog commands; only the platform-wide supported set is.
6. **Orphaned files.** Removing an image, replacing a video, an eleventh upload (the file is stored *before* `Product.AddImage` throws) and any failed save all leave bytes behind. `IFileStorage` has no delete, and the cleanup job is DEFERRED.
7. **The best-selling sort aggregates `OrderItems` per request** across all delivered orders; there is no read model or covering index for it, and it crosses a module boundary.
8. **`ProductDto.StockQuantity` shows the exact available quantity to anonymous visitors,** even though `InventoryItemDto` was deliberately kept separate so that on-hand, reserved and thresholds stay admin-only.
9. **Search is a plain substring match** with no full-text index, ranking, or diacritic handling, and case behaviour follows the database collation.
10. **`Newest` means id descending in the storefront but `CreatedAt` in the admin list** — the two lists can disagree for rows created in the same transaction.
11. **`POST /api/products` returns a Location header pointing at the public detail route**, which answers 404 while the product is a draft.
12. ~~The storefront routes by numeric id.~~ **Resolved in Phase 16:** the route is `/products/:handle` and accepts either. A slug loads through `api.getProductBySlug`; a numeric id still loads and is then replaced in the address bar with the slug form, so links shared before the change keep working. Product links are built by `productPath` in `frontend/src/features/catalog/productRouting.js`.
13. ~~The Offers page does not filter on sale.~~ **Resolved in Phase 16:** `frontend/src/pages/Offers.jsx` passes `onSale`, `useCatalog` forwards it, and the home page's offers row asks for the same filter.
14. **Descriptions are plain text** (DEFERRED, not scheduled: Phase 16 kept rendering them as escaped plain text, so the sanitizer dependency decision a rich description needs was never taken).
15. **`PUT /api/products/{id}` replaces every translation**, so a client that omits a language deletes it.

## Future evolution

- **PLANNED** ([ADR-0039](../../11-ADR/0039-product-variants-order-identity.md), [ADR-0040](../../11-ADR/0040-product-option-model.md), [ProductVariants.md](ProductVariants.md)): the storefront selection (V3), once the owner confirms two presentation questions. Variant images and manual variant ordering are not scheduled.
- **DEFERRED** ([ADR-0025](../../11-ADR/0025-catalog-model.md)): free-form product attributes beyond variant options; a sanitized rich description; image resizing/re-encoding (a dependency decision); the orphaned-file cleanup job.
- **PLANNED** ([ProductRoadmap.md](../../12-ROADMAP/ProductRoadmap.md)): per-tenant SEO beyond the page (a sitemap, and server rendering so a crawler that runs no JavaScript sees the metadata at all); catalog caching, image optimisation and a CDN (Phase 21); cloud blob storage (D-18, Phase 23).
- **Not built in the storefront yet:** a variant picker. Options and variants exist since V2, but `ProductDto` exposes no option or variant list until V3. Everything beneath it is done: the order records the variant and its label, and basket, checkout and stock accept one. Status, decisions and the remaining plan: [ProductVariants.md](ProductVariants.md).
- **FUTURE** (not scheduled): a catalog read contract for Shopping and Notifications so they stop loading the `Product` aggregate; moving the best-selling sort behind an Ordering contract or a Reporting read model; extracting search ([Architecture.md](../../02-ARCHITECTURE/Architecture.md)).
