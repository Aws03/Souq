# Catalog: change guide

> Read [README.md](README.md) first. This page lists the changes engineers actually make in this module and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change**

- A product is never hard-deleted. `DELETE` archives, and every other module's foreign key to `Products` is `Restrict`.
- Every product has exactly one default `ProductVariant`, guaranteed by the constructor and by the filtered unique index `IX_ProductVariants_ProductId_Default`. Price, compare-at price and SKU live on that variant, not on the product.
- Slugs are unique per store, SKUs are unique per store when set — checked in the handler for a clear message and enforced by unique indexes as the last line of defence.
- Business rules live in `Product`, `ProductVariant`, `Category` and `CatalogText`, not in handlers or controllers. Child entities are created and mutated only through their root (`internal` constructors).
- The text in the store's default culture is required; `SetTexts` replaces the whole set.
- Stock is not Catalog's. Nothing in `Features/Products` or `Features/Categories` may reference `Features/Inventory` — the only link is Catalog's own port `IVariantStockInitializer`.
- Every write is tenant-scoped and audited; storefront reads only ever return `Active` products in active categories.

**Files to read first**

`src/Souq.Domain/Entities/Product.cs`, `src/Souq.Domain/Entities/ProductParts.cs`, `src/Souq.Domain/Entities/Category.cs`, `src/Souq.Domain/Entities/CatalogTranslation.cs`, `src/Souq.Domain/ValueObjects/CatalogText.cs`, `src/Souq.Application/Features/Products/Commands/CreateProductHandler.cs`, `src/Souq.Application/Features/Products/Queries/ICatalogQueries.cs`, `src/Souq.Infrastructure/Persistence/Queries/CatalogQueries.cs`, `src/Souq.Infrastructure/Persistence/Configurations/ProductConfiguration.cs`, `src/Souq.API/Controllers/ProductsController.cs`, `src/Souq.API/Controllers/AdminCatalogController.cs`.

**Tests that guard the module**

`tests/Souq.Domain.Tests/ProductTests.cs`, `tests/Souq.Application.Tests/Products/ProductHandlersTests.cs`, `tests/Souq.Application.Tests/Products/UploadProductMediaHandlerTests.cs`, `tests/Souq.Application.Tests/Categories/CategoryHandlersTests.cs`, `tests/Souq.IntegrationTests/CatalogTests.cs` (including the fixed SQL-command-count assertion), `tests/Souq.IntegrationTests/QueryServiceTests.cs`, `tests/Souq.IntegrationTests/TenantIsolationTests.cs`, `tests/Souq.IntegrationTests/UploadSecurityTests.cs`, `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`, and the Vitest files under `frontend/src/features/catalog` and `frontend/src/features/admin`.

Run `dotnet test` and, for frontend logic, `npm test` in `frontend`.

---

## I need to add a field to products (for example a weight or a barcode)

- **Inspect:** `Product`, `ProductConfiguration`, `CreateProductCommand`/`UpdateProductCommand` and their validators, `ProductDto`/`AdminProductDto` in `src/Souq.Application/Features/Products/Queries/ProductDto.cs`, `CatalogQueries`, `frontend/src/features/admin/products/productPayload.js`, `frontend/src/pages/admin/ProductFormDrawer.jsx`.
- **Rules to respect:** the field's validity rule belongs in `Product` (a setter method that throws `InvalidProductDataException`, like `SetBrand`), with a length or range constant next to `SlugMaxLength`. The validator only repeats the *shape* (presence, maximum length) so a bad request is a 400 rather than a 422. Decide first whether the field describes the product (→ `Product`) or the sellable unit (→ `ProductVariant`, and then it must be set through `Product.SetPricing`-style delegation).
- **Steps:**
  1. Add the private-setter property and its guard method to `Product`; call it from the constructor if it is mandatory.
  2. Map it in `ProductConfiguration` with an explicit maximum length.
  3. Add it to `CreateProductCommand`, `UpdateProductCommand` and their validators; set it in `CreateProductHandler` and `UpdateProductHandler`.
  4. Add it to the DTOs the screens need — `AdminProductDto` always, `ProductDto` only if the storefront shows it — and project it in `CatalogQueries` inside the existing `Row`/`ToPageAsync` projections, never as a second query per row.
  5. Extend the admin form and `buildProductPayload`.
- **Tests:** a Domain test for the new guard; extend the create/update handler tests; extend `CatalogTests` if the field is returned by the API; extend `productPayload.test.js`.
- **API:** additive — a new property in the request and response bodies. Nothing breaks for existing clients. If the field is required, it is a breaking change for API callers: prefer optional with a default.
- **Database:** yes, a migration (`dotnet ef migrations add …`). Additive and reversible when the column is nullable or has a default; a `NOT NULL` column on an existing table needs a default or a backfill.
- **Security:** nothing new unless the field is free text shown in emails or notifications; keep lengths bounded. Add it to the audit metadata only if it matters for after-the-fact investigation.
- **Docs and ADR:** update this module's README (Domain model and Data ownership tables) and [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md). No ADR for a plain field.

## I need to add product attributes, or real variant options (size, colour)

The decisions are taken, and V1 (groundwork) and V2 (options and admin) are built. **Start with [ProductVariants.md](ProductVariants.md)** (status and remaining phases), **[ADR-0039](../../11-ADR/0039-product-variants-order-identity.md)** (P-08 and V1) **and [ADR-0040](../../11-ADR/0040-product-option-model.md)** (the option model as built).

- **Inspect:** `Product.SetOptions`, `Product.AddVariant`, `Product.UpdateVariant`, `Product.SetDefaultVariant`, `Product.VariantLabel`; `src/Souq.Domain/Entities/ProductOptions.cs` (`OptionNames`, `VariantLabels`); `ProductVariantCommands.cs`; `IProductRepository.GuardConcurrentEdit`; `ProductConfiguration.cs` (the combination key index, the restrict FK to values); `CatalogQueries.VisibleProducts` and `WishlistQueries` (the temporary single-active-variant gate, BR-CAT-24); `frontend/src/features/admin/products/variantModel.js` and `frontend/src/pages/admin/ProductVariants.jsx`.
- **Rules to respect:** BR-CAT-01 and BR-CAT-18 to BR-CAT-24. The default variant stays and stays active. A variant is never deleted, and a value any variant uses is never removed. Every structural edit goes through `Product` and calls `GuardConcurrentEdit` before saving. Every variant needs its own `InventoryItem`, opened through `IVariantStockInitializer` in the same transaction; Catalog must still not reference Inventory. Limits live in `Product` and the validators and are published through `VariantLimits`, never copied into the frontend.
- **Changing an option rule** (a limit, name rule, removal rule): change `Product.SetOptions`/`AddVariant` and `ProductOptionTests` first, then the validators, then the mirror in `variantModel.js` (it is UX only). A new limit value is an owner decision (P-08a), not a constant change.
- **Adding a variant attribute** (an image, a sort order): a column on `ProductVariants` changed only through `Product`, an additive migration, the admin DTO and page, and — if shoppers see it — V3.
- **Steps (V3, storefront):** remove the gate in `CatalogQueries.VisibleProducts` and `WishlistQueries`; `ProductDto` options and variants; the selector with an explicit choice and disabled sold-out values; "From" pricing in lists, filters, sort and structured data; variant-keyed basket calls; labels in cart, checkout, order pages and email. Keep `ToPageAsync`'s single-statement shape: `CatalogTests` asserts a constant SQL command count. The two presentation questions in [ProductVariants.md](ProductVariants.md) §10 must be answered first.
- **Tests:** Domain for every invariant; handlers in `ProductVariantHandlersTests`; `ProductOptionAdminTests` for the workflow, constraints and concurrency; a row in `TenantIsolationTests` for any new route with an id; the browser journey `frontend/e2e/product-variants.spec.js`.
- **Docs and ADR:** this README, [ProductVariants.md](ProductVariants.md), [BusinessRules.md](../../01-REQUIREMENTS/BusinessRules.md) and the roadmap's Phase 16 line; a new ADR if the model changes.

## I need to change the slug or SKU rules

- **Inspect:** `CatalogSlug.TryNormalize` and `Product.SlugMaxLength`/`Category.SlugMaxLength`; `ProductVariant` SKU normalisation; `ProductSlugs.Suggest` in `CreateProductHandler`; `SlugRule` in `src/Souq.Application/Features/Categories/Commands/CategoryValidators.cs`; the unique indexes in `ProductConfiguration`, `ProductVariantConfiguration` and `CategoryConfiguration`; `frontend/src/features/admin/products/productPayload.js` and `categoryForm.js`, which both lower-case the slug before sending.
- **Rules to respect:** the pattern exists in **two** places — `CatalogSlug` (Domain, 422 `InvalidProductData`/`InvalidCategory`) and `SlugRule.Pattern` (the category validators, 400 `ValidationFailed`). Change both or the two paths disagree. A relaxed pattern must stay URL-safe and must not allow a value that the storefront cannot route. Uniqueness stays per store: never make an index global.
- **Steps:**
  1. Change `CatalogSlug.TryNormalize` (and the SKU regex in `ProductVariant.NormalizeSku`) and the length constants.
  2. Mirror the pattern in `SlugRule`, or better, delete the duplicate and let the entity own it, accepting that category slug errors become 422.
  3. If lengths change, update `ProductConfiguration`/`CategoryConfiguration` and the validators.
  4. Check `ProductSlugs.Suggest`: it truncates to `Product.SlugMaxLength - 6` before appending a numeric suffix.
- **Tests:** `ProductTests` and `CategoryTests` have `[Theory]` cases for rejected slugs; `CreateProductHandlerTests` covers suggestion and suffixing; `CatalogTests` covers slug lookup and normalisation; `TenantIsolationTests` covers per-store uniqueness. Add cases for the new pattern, including one that used to be valid.
- **API:** existing rows are **not** re-validated. A stricter rule leaves old slugs in place until someone edits that product, and then the edit fails. Decide explicitly: reject on edit, or normalise on read.
- **Database:** no migration for a pattern change; a migration only if maximum lengths change. A backfill is a data migration and needs a plan for collisions.
- **Security:** slugs appear in URLs; keep them free of characters that need escaping.
- **Docs and ADR:** README (Business concepts and Domain model) and the options table in [ADR-0025](../../11-ADR/0025-catalog-model.md) if the decision itself changes.

## I need to add or change a category rule

- **Inspect:** `Category` (`MoveTo`, `MaxDepth`, `SetSortOrder`, `Activate`/`Deactivate`), `CategoryTree.AncestryOf`/`SubtreeHeight` in `src/Souq.Application/Features/Categories/Commands/CreateCategoryCommand.cs`, `ICategoryRepository.ListLinksAsync`, `DeleteCategoryHandler`, `frontend/src/features/admin/categories/categoryForm.js`.
- **Rules to respect:** the entity decides; the handler only supplies the facts it cannot know (the proposed parent's ancestry and the moving subtree's height, both derived from one tenant-filtered read of `(Id, ParentId)` pairs). Deletion stays guarded by `CategoryInUse` and `CategoryHasChildren` rather than letting a foreign key fail. `CategoryTree` must keep its cycle guard: it stops when it revisits an id.
- **Steps:**
  1. Add the rule to `Category` with its own exception type or message (`InvalidCategoryException` = data, `InvalidCategoryParentException` = tree shape).
  2. If the rule needs tree knowledge, extend `CategoryTree` and pass the answer in; do not query the database from the entity.
  3. Mirror any rule the admin UI can pre-empt in `categoryForm.js` (it already hides a category and its descendants from the parent picker) — the server stays the authority.
- **Tests:** `CategoryTests` for the entity; `CreateCategoryHandlerTests`/`UpdateCategoryHandlerTests` for the coordination; `CatalogTests` covers cycles and depth end to end; `categoryForm.test.js` for the tree helpers.
- **API:** the error code is the contract. Reuse `InvalidParent` for shape errors so the frontend keeps translating it.
- **Database:** usually none. A rule about the number of children or roots may want an index, not a constraint.
- **Security:** hiding a category is a visibility decision, not a permission one — see the next scenario before relying on it.
- **Docs and ADR:** README (Domain model, Known limitations) and [BusinessRules.md](../../01-REQUIREMENTS/BusinessRules.md).

## I need to change what "visible in the storefront" means

This is the highest-value clean-up in the module: the rule exists twice with two meanings (README, Known limitations 2 and 3).

- **Inspect:** `CatalogQueries.VisibleProducts()`, `WishlistQueries` (same predicate, copied), `Product.IsActive`, `PricingService.Price` (it sets `Sellable` from `IsActive`), `AddBasketItemHandler`, the wishlist handlers in `src/Souq.Application/Features/Wishlist/WishlistUseCases.cs`, `GetCategoriesHandler`/`ListCategoriesAsync`.
- **Rules to respect:** whatever the rule becomes, the *storefront read path* and the *sellability check* must agree, otherwise a customer can buy something they cannot see. If ancestors start to matter, the check must not become a per-row recursive query on a hot path.
- **Steps:**
  1. Write the rule down in one place. Options: a shared predicate expression used by both query services, or a persisted *effective visibility* column maintained whenever a category's visibility changes.
  2. Make `Product.IsActive`'s callers ask the same question. The cheapest correct move is to give Catalog a read contract (for example a *sellable snapshot*) that Shopping calls instead of loading the aggregate — this also removes the domain-level boundary leak.
  3. If ancestors count, decide what `GET /api/categories` returns for an active child of a hidden parent (today it returns it, and `orderAsTree` in the frontend then shows it as a root).
- **Tests:** extend `CatalogTests` ("a hidden category hides its products") with a grandchild case; add a `BasketTests` case proving an invisible product cannot be added; keep the SQL-command-count assertion green.
- **API:** a product that disappears from listings must also 404 on detail, and baskets that already contain it must flag it (Shopping already flags unsellable lines at checkout).
- **Database:** none for a shared predicate; a migration plus a backfill if you persist an effective visibility flag, and then every category visibility change has to maintain it.
- **Security:** a hidden product is not a protected resource; do not use visibility as an authorization mechanism.
- **Docs and ADR:** README (Known limitations), [Modules.md](../Modules.md) and, if a contract appears, an ADR recording it.

## I need to add a storefront sort or filter

- **Inspect:** `ProductSortBy`, `ProductSearch` and `ICatalogQueries.SearchProductsAsync`, `CatalogQueries.Sort`, `GetProductsQuery`/`GetProductsQueryValidator`, `ProductsController.GetAll`, `frontend/src/components/catalog/Catalog.jsx` (`SORT_API`) and `frontend/src/hooks/useCatalog.js`.
- **Rules to respect:** sorting is an allowlist — the client sends an enum value, never a column name. Every ordering must end with a tie-breaker on the id, or paging duplicates and drops rows (`QueryServiceTests` asserts this). Filters are typed fields on `ProductSearch`, not a generic query language, and `IQueryable` never crosses out of Infrastructure.
- **Steps:**
  1. Add the enum member (or the field on `ProductSearch` and `GetProductsQuery`).
  2. Handle it in `CatalogQueries.Sort` (or as a `Where` before the sort), keeping everything inside the one statement that `ToPageAsync` executes.
  3. Bound it in `GetProductsQueryValidator` (`IsInEnum`, ranges, collection sizes).
  4. Expose it in the controller's query parameters and wire the UI.
  5. Check the index that will serve it; the existing ones lead with `TenantId`.
- **Tests:** `GetProductsHandlerTests` proves the value reaches the read port; add a `QueryServiceTests` case for ordering and paging stability. If the sort reads another module's tables (as `BestSelling` does), say so in the README rather than hiding it.
- **API:** additive; an unknown value already fails validation with 400 instead of being silently ignored.
- **Database:** possibly an index. Measure first: [ProductRoadmap.md](../../12-ROADMAP/ProductRoadmap.md) puts query plans and indexes in Phase 21.
- **Security:** none, but keep filters bounded (the keyword is capped at 200 characters and category lists at 50 entries for a reason).
- **Docs and ADR:** README (API section, the sort table) and [Endpoints.md](../../05-API/Endpoints.md).

## I need to add image processing, or move media to cloud storage

- **Inspect:** `IFileStorage`, `LocalFileStorage`, `FileStorageOptions`, `MediaFileInspector`, `UploadProductImageHandler`, `UploadProductVideoHandler`, the `/uploads` static-file setup in `src/Souq.API/Program.cs`, and the tenant guard in `src/Souq.API/Tenancy/TenantResolutionMiddleware.cs`.
- **Rules to respect:** the stored extension comes from the detected type, never from the client ([ADR-0016](../../11-ADR/0016-upload-validation.md)). The adapter never sees a client filename. Keys stay tenant-prefixed (`tenants/{tenantId}/…`) so the host guard keeps working. Processing must not put a slow external call inside a database transaction ([ADR-0021](../../11-ADR/0021-transaction-boundaries.md)) — today the upload handler saves the file and then `SaveChangesAsync` without an explicit transaction.
- **Steps (processing):**
  1. Decide the dependency (it is a DEFERRED decision in [ADR-0025](../../11-ADR/0025-catalog-model.md)) and record it in an ADR.
  2. Put resizing behind a port in Application (for example an image-processing port next to `IFileStorage`) and implement it in Infrastructure; keep `MediaFileInspector` as the gate that runs first.
  3. Decide where derived sizes live: extra keys next to the original, or a variant list on `ProductImage` (a migration).
  4. Re-encoding is also a security control (it strips polyglots); if you add it, say so in [Security.md](../../07-SECURITY/Security.md).
- **Steps (cloud storage):** implement `IFileStorage` against the provider, register it in `AddStorage` in `src/Souq.Infrastructure/DependencyInjection.cs`, keep the returned public path shape or teach the frontend the new one, and decide how the tenant guard is enforced when files are no longer served by this API (signed URLs or a per-tenant prefix on a CDN).
- **Tests:** `MediaFileInspectorTests` and `UploadProductMediaHandlerTests` stay the contract for detection; `LocalFileStorageTests` pins the key shape; `UploadSecurityTests` pins the serving headers and that a non-media file is never served. Add tests for the new adapter behind the same port.
- **API:** the image response already returns the stored URL, so extra sizes need a response change (`ProductImageDto`).
- **Database:** only if derived sizes are stored as rows.
- **Security:** the highest-risk area in the module. Keep the allowlist, the sniffing, `ServeUnknownFileTypes = false`, `nosniff` and the sandbox CSP. Never trust `folder`/`extension` from a caller — `LocalFileStorage` validates both with regexes.
- **Docs and ADR:** a new ADR (dependency choice), [ADR-0016](../../11-ADR/0016-upload-validation.md) revisit note, README (External integrations) and [Configuration.md](../../09-OPERATIONS/Configuration.md) for `Storage:Local:RootPath`.

## I need to add a language, or enforce the store's enabled languages

- **Inspect:** `Tenant.SupportedCultures`, `StoreSettings.EnabledCultures`, `CatalogText.NormalizeAll`, `CatalogTexts.HasCulture`, `CatalogTranslation.NameIn`, `CatalogTranslationMapping` (`CultureMaxLength` and the unique index per culture), `frontend/src/features/catalog/catalogText.js` (`CATALOG_CULTURES`).
- **Rules to respect:** adding a platform language is a Platform decision — `Tenant.SupportedCultures` is the gate, and catalog text validation follows it. The store's default culture is mandatory on every product and category. `SetTexts` removes any culture that is not sent, so widening the language set never deletes data but narrowing it can.
- **Steps (new platform language):** extend `Tenant.SupportedCultures`, add the culture to `CATALOG_CULTURES` and to the i18n resources, and check `NameIn`'s ordinal fallback still picks a sensible language.
- **Steps (enforce enabled cultures):** pass the store's enabled set into the command handlers (`ITenantContext` already exposes the tenant; `EnabledCultures` lives on `StoreSettings`), reject unknown cultures with a validation error rather than a domain exception, and decide what happens to translations already stored for a culture the store later disables — they are currently kept and still returned by the API.
- **Tests:** `ProductTests` covers "texts in supported languages, replaced as a set"; add a handler test for the rejection; `CatalogTests` round-trips translations through the API.
- **API:** stricter validation is a breaking change for admin clients that send both languages regardless of the store's settings, which is exactly what the current admin form does (`formToTexts` sends every culture that has a name).
- **Database:** none. Culture columns are already `CultureMaxLength` wide with a unique index per owner.
- **Security:** none.
- **Docs and ADR:** README (Tenant behaviour), [WhiteLabel.md](../../08-FRONTEND/WhiteLabel.md) and [MultiTenancy.md](../../02-ARCHITECTURE/MultiTenancy.md).
