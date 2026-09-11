using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Categories.Queries;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// CatalogQueries — تنفيذ منفذ القراءة ICatalogQueries (ADR-0008): AsNoTracking + إسقاط
// مباشر إلى ProductDto (اسم الفئة بـ JOIN في الاستعلام نفسه، لا Include لكيان كامل).
// ============================================================================
internal sealed class CatalogQueries : ICatalogQueries
{
    private readonly AppDbContext _db;
    public CatalogQueries(AppDbContext db) => _db = db;

    private static readonly Expression<Func<Product, ProductDto>> ToProductDto = p => new ProductDto(
        p.Id, p.NameAr, p.NameEn, p.Description,
        p.Price.Amount, p.Price.Currency,
        p.StockQuantity, p.ImageUrl, p.VideoUrl,
        p.CategoryId, p.Category!.Name);

    public Task<PaginatedList<ProductDto>> SearchProductsAsync(ProductSearch search, PageRequest page, CancellationToken ct)
    {
        var query = ActiveProducts();

        // Name خاصية محسوبة غير مُعيَّنة لعمود — نبحث في NameAr وNameEn المُعيَّنين فعلياً،
        // فيطابق البحث أيّاً من اللغتين. Contains مع مُعامِل يُترجم إلى CHARINDEX (لا حقن LIKE).
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            query = query.Where(p =>
                p.NameAr.Contains(keyword) || p.NameEn.Contains(keyword) || p.Description.Contains(keyword));
        }
        if (search.CategoryIds is { Count: > 0 } categoryIds)
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        if (search.MinPrice is decimal min)
            query = query.Where(p => p.Price.Amount >= min);
        if (search.MaxPrice is decimal max)
            query = query.Where(p => p.Price.Amount <= max);

        return Sort(query, search.SortBy).ToPageAsync(ToProductDto, page, ct);
    }

    public Task<ProductDto?> FindActiveProductAsync(int id, CancellationToken ct) =>
        ActiveProducts().Where(p => p.Id == id).Select(ToProductDto).FirstOrDefaultAsync(ct);

    // نفس الفئة أولاً (الأكثر مبيعاً)، ثم أحدث منتجات الفئات الأخرى إن لم تكفِ — استعلامان
    // صغيران محدودان بـ count، لا تحميل الكتالوج.
    public async Task<IReadOnlyList<ProductDto>?> FindRelatedProductsAsync(int productId, int count, CancellationToken ct)
    {
        var categoryId = await ActiveProducts().Where(p => p.Id == productId)
            .Select(p => (int?)p.CategoryId).FirstOrDefaultAsync(ct);
        if (categoryId is null) return null;

        var sameCategory = await BestSellingFirst(ActiveProducts()
                .Where(p => p.CategoryId == categoryId && p.Id != productId))
            .Take(count).Select(ToProductDto).ToListAsync(ct);
        if (sameCategory.Count >= count) return sameCategory;

        var excluded = sameCategory.Select(p => p.Id).Append(productId).ToList();
        var others = await ActiveProducts()
            .Where(p => p.CategoryId != categoryId && !excluded.Contains(p.Id))
            .OrderByDescending(p => p.Id)
            .Take(count - sameCategory.Count).Select(ToProductDto).ToListAsync(ct);

        return sameCategory.Concat(others).ToList();
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct) =>
        await _db.Categories.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.ParentId))
            .ToListAsync(ct);

    private IQueryable<Product> ActiveProducts() => _db.Products.AsNoTracking().Where(p => p.IsActive);

    // كل ترتيب ينتهي بكاسر تعادل بالمعرّف: منتجان بنفس السعر (أو بلا مبيعات) لا يتبادلان
    // موقعيهما بين طلبين، فلا يتكرّر منتج بين صفحتين ولا يختفي.
    private IOrderedQueryable<Product> Sort(IQueryable<Product> query, ProductSortBy sortBy) => sortBy switch
    {
        ProductSortBy.PriceAsc => query.OrderBy(p => p.Price.Amount).ThenByDescending(p => p.Id),
        ProductSortBy.PriceDesc => query.OrderByDescending(p => p.Price.Amount).ThenByDescending(p => p.Id),
        ProductSortBy.BestSelling => BestSellingFirst(query),
        _ => query.OrderByDescending(p => p.Id),
    };

    // "الأكثر مبيعاً" = مجموع الكميات عبر الطلبات المُسلَّمة فقط — استعلام فرعي مترابط واحد
    // في SQL يستخدم الفهرس IX_OrderItems_ProductId، لا تحميل بيانات للذاكرة.
    private IOrderedQueryable<Product> BestSellingFirst(IQueryable<Product> query) =>
        query.OrderByDescending(p =>
                _db.Orders.Where(o => o.Status == OrderStatus.Delivered)
                    .SelectMany(o => o.Items)
                    .Where(i => i.ProductId == p.Id)
                    .Sum(i => (int?)i.Quantity) ?? 0)
            .ThenByDescending(p => p.Id);
}
