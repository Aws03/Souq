using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مستودع المنتجات: يضيف الاستعلامات المتخصّصة (البحث، التصفية، الترقيم).
public class ProductRepository : RepositoryBase<Product>, IProductRepository
{
    public ProductRepository(AppDbContext db) : base(db) { }

    public async Task<Product?> GetActiveByIdAsync(int id, CancellationToken ct = default)
        => await Db.Products.Include(p => p.Category)
                            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, ct);

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId, CancellationToken ct = default)
        => await Db.Products.Where(p => p.CategoryId == categoryId && p.IsActive)
                            .Include(p => p.Category).ToListAsync(ct);

    public async Task<(IReadOnlyList<Product>, int)> SearchAsync(
        string? keyword, IReadOnlyCollection<int>? categoryIds, int page, int pageSize,
        decimal? minPrice = null, decimal? maxPrice = null,
        ProductSortBy sortBy = ProductSortBy.Newest, CancellationToken ct = default)
    {
        // نبني الاستعلام تدريجياً حسب المعايير المتوفّرة (Query Composition).
        var query = Db.Products.Include(p => p.Category).Where(p => p.IsActive);

        // Name خاصية محسوبة غير مُعيَّنة لعمود (انظر Ignore في ProductConfiguration)
        // فلا يمكن لـ EF ترجمتها ضمن استعلام — نبحث في NameAr وNameEn المُعيَّنين
        // فعلياً، ما يجعل البحث يطابق أيّاً من اللغتين مجاناً.
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(p =>
                p.NameAr.Contains(keyword) || p.NameEn.Contains(keyword) || p.Description.Contains(keyword));
        if (categoryIds is { Count: > 0 })
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        if (minPrice.HasValue)
            query = query.Where(p => p.Price.Amount >= minPrice.Value);
        if (maxPrice.HasValue)
            query = query.Where(p => p.Price.Amount <= maxPrice.Value);

        var total = await query.CountAsync(ct);   // العدد الكلي قبل الترقيم (للأزرار)

        // "الأكثر مبيعاً" = مجموع الكميات عبر الطلبات المُسلَّمة فقط (Delivered —
        // المبيعات المؤكّدة فعلاً، لا الملغاة ولا المعلّقة). لا عمود OrderId على
        // OrderItem (مفتاح ظلّ، انظر OrderConfiguration) فنصل عبر Orders.SelectMany
        // — يُترجم إلى استعلام فرعي مترابط واحد، لا تحميل بيانات للذاكرة.
        // ThenByDescending(Id) يكسر التعادل (وأصفار المبيعات) بثبات: الأحدث أولاً.
        var ordered = sortBy switch
        {
            ProductSortBy.PriceAsc => query.OrderBy(p => p.Price.Amount).ThenByDescending(p => p.Id),
            ProductSortBy.PriceDesc => query.OrderByDescending(p => p.Price.Amount).ThenByDescending(p => p.Id),
            ProductSortBy.BestSelling => query.OrderByDescending(p =>
                    Db.Orders.Where(o => o.Status == OrderStatus.Delivered)
                             .SelectMany(o => o.Items)
                             .Where(i => i.ProductId == p.Id)
                             .Sum(i => (int?)i.Quantity) ?? 0)
                .ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.Id),
        };

        // الترقيم الفعلي: نتخطّى صفحات سابقة ونأخذ صفحة واحدة فقط.
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
                                 .ToListAsync(ct);
        return (items, total);
    }

    // نشمل المعطّلة (لا فلتر IsActive): المفتاح الأجنبي قائم بغضّ النظر عنه.
    public async Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default)
        => await Db.Products.AnyAsync(p => p.CategoryId == categoryId, ct);

    // جرد المخزون: المنتجات النشطة مع فئاتها، الأقلّ مخزوناً أولاً (المنتجات
    // الحرجة في الأعلى). كسر التعادل بالمعرّف كي يبقى الترتيب ثابتاً.
    public async Task<IReadOnlyList<Product>> GetInventoryAsync(CancellationToken ct = default)
        => await Db.Products.Include(p => p.Category).Where(p => p.IsActive)
                            .OrderBy(p => p.StockQuantity).ThenBy(p => p.Id)
                            .ToListAsync(ct);

    // المنخفض مخزونه: المخزون بلغ حدّ التنبيه أو نزل تحته. نكتب المقارنة صراحةً
    // (لا الخاصية المحسوبة IsLowStock) كي يترجمها EF إلى SQL على العمودين.
    public async Task<IReadOnlyList<Product>> GetLowStockAsync(CancellationToken ct = default)
        => await Db.Products.Include(p => p.Category)
                            .Where(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold)
                            .OrderBy(p => p.StockQuantity).ThenBy(p => p.Id)
                            .ToListAsync(ct);

    // منتجات ذات صلة: نفس منطق "الأكثر مبيعاً" في SearchAsync (مجموع الكميات
    // عبر الطلبات المُسلَّمة فقط) لكن مقصوراً على فئة المنتج الحالي كي تكون
    // الاقتراحات فعلاً مشابهة. إن لم تكفِ نفس الفئة، نُكمل بأحدث منتجات من فئات
    // أخرى (تنويع) بدل إرجاع عدد أقلّ من count.
    public async Task<IReadOnlyList<Product>> GetRelatedAsync(
        int productId, int categoryId, int count, CancellationToken ct = default)
    {
        var sameCategory = await Db.Products.Include(p => p.Category)
            .Where(p => p.IsActive && p.CategoryId == categoryId && p.Id != productId)
            .OrderByDescending(p =>
                Db.Orders.Where(o => o.Status == OrderStatus.Delivered)
                         .SelectMany(o => o.Items)
                         .Where(i => i.ProductId == p.Id)
                         .Sum(i => (int?)i.Quantity) ?? 0)
            .ThenByDescending(p => p.Id)
            .Take(count)
            .ToListAsync(ct);

        if (sameCategory.Count >= count) return sameCategory;

        var excludedIds = sameCategory.Select(p => p.Id).Append(productId).ToList();
        var others = await Db.Products.Include(p => p.Category)
            .Where(p => p.IsActive && p.CategoryId != categoryId && !excludedIds.Contains(p.Id))
            .OrderByDescending(p => p.Id)
            .Take(count - sameCategory.Count)
            .ToListAsync(ct);

        return sameCategory.Concat(others).ToList();
    }
}
