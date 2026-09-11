using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مستودع المنتجات لجهة الكتابة فقط؛ القراءات في CatalogQueries/InventoryQueries. التجمّع يُحمَّل بأبنائه كلهم
// (استعلامات منفصلة بدل JOIN يضاعف الصفوف ترجماتٍ × صوراً × متغيّرات).
public class ProductRepository : RepositoryBase<Product>, IProductRepository
{
    public ProductRepository(AppDbContext db) : base(db) { }

    public override Task<Product?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Db.Products
            .Include(p => p.Translations)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    // بأي حالة (مسودّة/مؤرشف أيضاً): المفتاح الأجنبي قائم بغضّ النظر عنها.
    public Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default)
        => Db.Products.AnyAsync(p => p.CategoryId == categoryId, ct);

    public Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct = default)
        => Db.Products.AnyAsync(p => p.Slug == slug && p.Id != exceptProductId, ct);

    public Task<bool> SkuExistsAsync(string sku, int? exceptProductId, CancellationToken ct = default)
        => Db.Set<ProductVariant>().AnyAsync(
            v => v.Sku == sku && EF.Property<int>(v, "ProductId") != exceptProductId, ct);
}
