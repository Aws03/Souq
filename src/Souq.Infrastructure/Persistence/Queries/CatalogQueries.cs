using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Categories.Queries;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// CatalogQueries — تنفيذ ICatalogQueries (ADR-0008): AsNoTracking + إسقاط مباشر إلى صفوف قراءة، ثم DTO في الذاكرة
// (قاموس اللغات، اختيار لغة المتجر). السعر وسعر المقارنة وSKU من المتغيّر الافتراضي (D-21) باستعلامات فرعية في
// SQL نفسه؛ الاسم بلغة مطلوبة وإلا أول لغة. المتجر: النشط في فئة مفعّلة فقط.
// ============================================================================
internal sealed class CatalogQueries : ICatalogQueries
{
    private readonly AppDbContext _db;
    public CatalogQueries(AppDbContext db) => _db = db;

    public async Task<PaginatedList<ProductDto>> SearchProductsAsync(
        ProductSearch search, PageRequest page, string culture, CancellationToken ct)
    {
        var query = VisibleProducts();

        // Contains مع مُعامِل يُترجم إلى CHARINDEX (لا حقن LIKE) — الاسم أو الوصف بأي لغة.
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            query = query.Where(p => p.Translations.Any(t =>
                t.Name.Contains(keyword) || (t.Description != null && t.Description.Contains(keyword))));
        }
        if (search.CategoryIds is { Count: > 0 } categoryIds)
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        if (search.MinPrice is decimal min)
            query = query.Where(p => p.Variants.Any(v => v.IsDefault && v.Price.Amount >= min));
        if (search.MaxPrice is decimal max)
            query = query.Where(p => p.Variants.Any(v => v.IsDefault && v.Price.Amount <= max));
        if (search.OnSaleOnly)
            query = query.Where(p => p.Variants.Any(v =>
                v.IsDefault && EF.Property<decimal?>(v, "_compareAtAmount") > v.Price.Amount));

        return (await Sort(query, search.SortBy).ToPageAsync(Row(culture), page, ct)).Map(r => ToDto(r, culture));
    }

    public async Task<ProductDto?> FindActiveProductAsync(int id, string culture, CancellationToken ct) =>
        await DetailAsync(VisibleProducts().Where(p => p.Id == id), culture, ct);

    public async Task<ProductDto?> FindActiveProductBySlugAsync(string slug, string culture, CancellationToken ct) =>
        await DetailAsync(VisibleProducts().Where(p => p.Slug == slug), culture, ct);

    // نفس الفئة أولاً (الأكثر مبيعاً)، ثم أحدث منتجات الفئات الأخرى إن لم تكفِ — استعلامان صغيران محدودان بـ count.
    public async Task<IReadOnlyList<ProductDto>?> FindRelatedProductsAsync(int productId, int count, string culture, CancellationToken ct)
    {
        var categoryId = await VisibleProducts().Where(p => p.Id == productId)
            .Select(p => (int?)p.CategoryId).FirstOrDefaultAsync(ct);
        if (categoryId is null) return null;

        var sameCategory = await BestSellingFirst(VisibleProducts()
                .Where(p => p.CategoryId == categoryId && p.Id != productId))
            .Take(count).Select(Row(culture)).ToListAsync(ct);
        if (sameCategory.Count >= count) return sameCategory.Select(r => ToDto(r, culture)).ToList();

        var excluded = sameCategory.Select(p => p.Id).Append(productId).ToList();
        var others = await VisibleProducts()
            .Where(p => p.CategoryId != categoryId && !excluded.Contains(p.Id))
            .OrderByDescending(p => p.Id)
            .Take(count - sameCategory.Count).Select(Row(culture)).ToListAsync(ct);

        return sameCategory.Concat(others).Select(r => ToDto(r, culture)).ToList();
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(bool includeInactive, string culture, CancellationToken ct)
    {
        var categories = _db.Categories.AsNoTracking();
        if (!includeInactive) categories = categories.Where(c => c.IsActive);

        var rows = await categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new CategoryRow(c.Id, c.Slug, c.ParentId, c.SortOrder, c.IsActive,
                c.Translations.Select(t => new TextRow(t.Culture, t.Name, t.Description, t.MetaTitle, t.MetaDescription)).ToList()))
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var texts = Texts(r.Texts);
            return new CategoryDto(r.Id, r.Slug, Pick(texts, culture)?.Name ?? r.Slug, texts, r.ParentId, r.SortOrder, r.IsActive);
        }).ToList();
    }

    public async Task<PaginatedList<AdminProductListItemDto>> ListAdminProductsAsync(
        AdminProductSearch search, PageRequest page, string culture, CancellationToken ct)
    {
        var query = _db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            var sku = keyword.ToUpperInvariant();
            query = query.Where(p => p.Slug.Contains(keyword)
                                     || p.Translations.Any(t => t.Name.Contains(keyword))
                                     || p.Variants.Any(v => v.Sku != null && v.Sku.Contains(sku)));
        }
        if (search.Status is { } status) query = query.Where(p => p.Status == status);
        if (search.CategoryId is int categoryId) query = query.Where(p => p.CategoryId == categoryId);

        IOrderedQueryable<Product> ordered = search.SortBy switch
        {
            AdminProductSortBy.NameAsc => query.OrderBy(NameExpr(culture)).ThenByDescending(p => p.Id),
            AdminProductSortBy.PriceAsc => query.OrderBy(PriceExpr).ThenByDescending(p => p.Id),
            AdminProductSortBy.PriceDesc => query.OrderByDescending(PriceExpr).ThenByDescending(p => p.Id),
            AdminProductSortBy.StockAsc => query
                .OrderBy(p => _db.InventoryItems.Where(s => s.ProductId == p.Id).Sum(s => (int?)(s.OnHand - s.Reserved)) ?? 0)
                .ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        };

        return await ordered.ToPageAsync(p => new AdminProductListItemDto(
            p.Id, p.Slug,
            p.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                ?? p.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault() ?? p.Slug,
            p.Status.ToString(),
            p.Variants.Where(v => v.IsDefault).Select(v => v.Sku).FirstOrDefault(),
            p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Amount).FirstOrDefault(),
            p.Variants.Where(v => v.IsDefault).Select(v => EF.Property<decimal?>(v, "_compareAtAmount")).FirstOrDefault(),
            p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Currency).FirstOrDefault() ?? "",
            _db.InventoryItems.Where(s => s.ProductId == p.Id).Sum(s => (int?)(s.OnHand - s.Reserved)) ?? 0,
            _db.InventoryItems.Where(s => s.ProductId == p.Id).Select(s => (int?)s.LowStockThreshold).Min() ?? 0,
            p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).FirstOrDefault(),
            p.CategoryId,
            p.Category!.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                ?? p.Category.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
            p.CreatedAt,
            p.Variants.Count()), page, ct);
    }

    // نموذج التعديل: التجمّع كاملاً (صف واحد) — استعلامات منفصلة للأبناء بدل ضرب الصفوف.
    public async Task<AdminProductDto?> FindAdminProductAsync(int id, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking()
            .Include(p => p.Translations).Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
            .Include(p => p.Options).ThenInclude(o => o.Translations)
            .Include(p => p.Options).ThenInclude(o => o.Values).ThenInclude(v => v.Translations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return null;

        // المخزون للعرض فقط (يُعدَّل بتصحيحات في وحدة Inventory): صفّ لكل متغيّر.
        var stock = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.ProductId == id)
            .Select(i => new { i.VariantId, i.OnHand, i.Reserved, i.LowStockThreshold })
            .ToDictionaryAsync(i => i.VariantId, ct);

        var options = product.Options.OrderBy(o => o.Position).ThenBy(o => o.Id).ToList();
        var valuePosition = options.SelectMany((o, index) => o.Values.Select(v => (v.Id, Rank: index * 100 + v.Position)))
            .ToDictionary(v => v.Id, v => v.Rank);

        // ترتيب المتغيّرات بقيمها وفق ترتيب الخيارات (S قبل M، ثم اللون) — مشتقّ لا مخزَّن.
        var variants = product.Variants
            .Select(v => (Variant: v, ValueIds: v.OptionValues.Select(ov => ov.OptionValueId)
                .OrderBy(valueId => valuePosition.GetValueOrDefault(valueId)).ToList()))
            .OrderBy(v => string.Join(',', v.ValueIds.Select(valueId => valuePosition.GetValueOrDefault(valueId).ToString("D5"))), StringComparer.Ordinal)
            .ThenBy(v => v.Variant.Id)
            .Select(v =>
            {
                var level = stock.GetValueOrDefault(v.Variant.Id);
                int onHandV = level?.OnHand ?? 0, reservedV = level?.Reserved ?? 0;
                return new AdminProductVariantDto(v.Variant.Id, v.Variant.IsDefault, v.Variant.IsActive, v.Variant.Sku,
                    v.Variant.Price.Amount, v.Variant.CompareAtPrice?.Amount, v.ValueIds,
                    onHandV, reservedV, onHandV - reservedV, level?.LowStockThreshold ?? 0);
            })
            .ToList();

        int onHand = variants.Sum(v => v.OnHand), reserved = variants.Sum(v => v.Reserved);
        var defaultVariant = product.DefaultVariant;

        return new AdminProductDto(
            product.Id, product.Slug, product.Status.ToString(),
            product.Translations.ToDictionary(t => t.Culture, t => new CatalogTextDto(t.Name, t.Description, t.MetaTitle, t.MetaDescription)),
            product.Sku, product.Price.Amount, product.CompareAtPrice?.Amount, product.Price.Currency,
            onHand, reserved, onHand - reserved, stock.GetValueOrDefault(defaultVariant.Id)?.LowStockThreshold ?? 0,
            product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => new ProductImageDto(i.Id, i.Url, i.SortOrder)).ToList(),
            product.VideoUrl, product.CategoryId, product.Brand, product.CreatedAt, product.UpdatedAt,
            options.Select(o => new AdminProductOptionDto(o.Id, o.Position, Names(o.Translations),
                o.Values.OrderBy(v => v.Position).ThenBy(v => v.Id)
                    .Select(v => new AdminProductOptionValueDto(v.Id, v.Position, Names(v.Translations))).ToList())).ToList(),
            variants, ProductVariantLimitsDto.Current);
    }

    private static IReadOnlyDictionary<string, string> Names(IEnumerable<OptionTranslation> translations) =>
        translations.OrderBy(t => t.Culture, StringComparer.Ordinal).ToDictionary(t => t.Culture, t => t.Name);

    // ── داخلي ───────────────────────────────────────────────────────────────

    // المعروض: نشط، في فئة مفعّلة، وله متغيّر ضمني (نشط واحد) — V2 مؤقتاً (ADR-0040): واجهة المتجر تشتري بمعرّف المنتج وحده
    // حتى يُبنى اختيار المتغيّر (V3)، فمنتج بأكثر من متغيّر نشط لا تستطيع بيعه لا يُعرض فيها بدل زرّ إضافة يفشل.
    private IQueryable<Product> VisibleProducts() =>
        _db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Category!.IsActive
                                               && p.Variants.Count(v => v.IsActive) == 1);

    private async Task<ProductDto?> DetailAsync(IQueryable<Product> product, string culture, CancellationToken ct)
    {
        var row = await product.Select(Row(culture)).FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var images = await _db.Set<ProductImage>().AsNoTracking()
            .Where(i => EF.Property<int>(i, "ProductId") == row.Id)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).ToListAsync(ct);
        return ToDto(row, culture, images);
    }

    private static readonly Expression<Func<Product, decimal>> PriceExpr =
        p => p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Amount).FirstOrDefault();

    private static Expression<Func<Product, string?>> NameExpr(string culture) =>
        p => p.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
             ?? p.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault();

    // المتاح للبيع من وحدة Inventory (المرحلة 6): الموجود − المحجوز، استعلام فرعي مترابط في SQL نفسه — للمتغيّرات النشطة وحدها:
    // مخزون متغيّر معطّل لا يُباع.
    private Expression<Func<Product, ProductRow>> Row(string culture) => p => new ProductRow(
        p.Id, p.Slug,
        p.Translations.Select(t => new TextRow(t.Culture, t.Name, t.Description, t.MetaTitle, t.MetaDescription)).ToList(),
        p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Amount).FirstOrDefault(),
        p.Variants.Where(v => v.IsDefault).Select(v => EF.Property<decimal?>(v, "_compareAtAmount")).FirstOrDefault(),
        p.Variants.Where(v => v.IsDefault).Select(v => v.Price.Currency).FirstOrDefault() ?? "",
        _db.InventoryItems.Where(s => s.ProductId == p.Id && p.Variants.Any(v => v.Id == s.VariantId && v.IsActive))
            .Sum(s => (int?)(s.OnHand - s.Reserved)) ?? 0,
        p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).FirstOrDefault(),
        p.VideoUrl, p.CategoryId,
        p.Category!.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
            ?? p.Category.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
        p.Brand);

    private static ProductDto ToDto(ProductRow r, string culture, IReadOnlyList<string>? images = null)
    {
        var texts = Texts(r.Texts);
        var main = Pick(texts, culture);
        return new ProductDto(
            r.Id, r.Slug, main?.Name ?? r.Slug, main?.Description, texts,
            r.Price, r.CompareAtPrice, r.Currency, r.StockQuantity,
            r.ImageUrl, images, r.VideoUrl, r.CategoryId, r.CategoryName, r.Brand);
    }

    private static IReadOnlyDictionary<string, CatalogTextDto> Texts(IEnumerable<TextRow> rows) =>
        rows.ToDictionary(t => t.Culture, t => new CatalogTextDto(t.Name, t.Description, t.MetaTitle, t.MetaDescription));

    private static CatalogTextDto? Pick(IReadOnlyDictionary<string, CatalogTextDto> texts, string culture) =>
        texts.TryGetValue(culture, out var text)
            ? text
            : texts.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Value).FirstOrDefault();

    // كل ترتيب ينتهي بكاسر تعادل بالمعرّف: منتجان بنفس السعر لا يتبادلان موقعيهما بين طلبين.
    private IOrderedQueryable<Product> Sort(IQueryable<Product> query, ProductSortBy sortBy) => sortBy switch
    {
        ProductSortBy.PriceAsc => query.OrderBy(PriceExpr).ThenByDescending(p => p.Id),
        ProductSortBy.PriceDesc => query.OrderByDescending(PriceExpr).ThenByDescending(p => p.Id),
        ProductSortBy.BestSelling => BestSellingFirst(query),
        _ => query.OrderByDescending(p => p.Id),
    };

    // "الأكثر مبيعاً" = مجموع الكميات عبر الطلبات المُسلَّمة فقط — استعلام فرعي مترابط واحد في SQL.
    private IOrderedQueryable<Product> BestSellingFirst(IQueryable<Product> query) =>
        query.OrderByDescending(p =>
                _db.Orders.Where(o => o.Status == OrderStatus.Delivered)
                    .SelectMany(o => o.Items)
                    .Where(i => i.ProductId == p.Id)
                    .Sum(i => (int?)i.Quantity) ?? 0)
            .ThenByDescending(p => p.Id);

    private sealed record TextRow(string Culture, string Name, string? Description, string? MetaTitle, string? MetaDescription);

    private sealed record ProductRow(
        int Id, string Slug, List<TextRow> Texts, decimal Price, decimal? CompareAtPrice, string Currency, int StockQuantity,
        string? ImageUrl, string? VideoUrl, int CategoryId, string? CategoryName, string? Brand);

    private sealed record CategoryRow(int Id, string Slug, int? ParentId, int SortOrder, bool IsActive, List<TextRow> Texts);
}
