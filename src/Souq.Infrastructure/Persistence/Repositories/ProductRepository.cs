using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مستودع المنتجات لجهة الكتابة فقط؛ القراءات في CatalogQueries/InventoryQueries. التجمّع يُحمَّل بأبنائه كلهم
// (استعلامات منفصلة بدل JOIN يضاعف الصفوف ترجماتٍ × صوراً × متغيّرات × خيارات).
public class ProductRepository : RepositoryBase<Product>, IProductRepository
{
    public ProductRepository(AppDbContext db) : base(db) { }

    public override Task<Product?> GetByIdAsync(int id, CancellationToken ct = default) =>
        WithChildren().FirstOrDefaultAsync(p => p.Id == id, ct);

    // عدد ثابت من الاستعلامات (واحد لكل مجموعة أبناء) مهما كثرت المنتجات.
    public async Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        ids.Count == 0 ? [] : await WithChildren().Where(p => ids.Contains(p.Id)).ToListAsync(ct);

    // الفئة مُضمَّنة لأن قابلية البيع تعتمد عليها (Product.IsSellable، R-07) — لا استعلام إضافي لكل منتج: الفئة انضمام
    // واحد على الجذر، والأبناء استعلاماتهم المنفصلة كما كانت.
    private IQueryable<Product> WithChildren() => Db.Products
        .Include(p => p.Category)
        .Include(p => p.Translations)
        .Include(p => p.Images)
        .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
        .Include(p => p.Options).ThenInclude(o => o.Translations)
        .Include(p => p.Options).ThenInclude(o => o.Values).ThenInclude(v => v.Translations)
        .AsSplitQuery();

    // بأي حالة (مسودّة/مؤرشف أيضاً): المفتاح الأجنبي قائم بغضّ النظر عنها.
    public Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default)
        => Db.Products.AnyAsync(p => p.CategoryId == categoryId, ct);

    public Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct = default)
        => Db.Products.AnyAsync(p => p.Slug == slug && p.Id != exceptProductId, ct);

    public Task<bool> SkuExistsAsync(string sku, int? exceptProductId, CancellationToken ct = default)
        => Db.Set<ProductVariant>().AnyAsync(
            v => v.Sku == sku && EF.Property<int>(v, "ProductId") != exceptProductId, ct);

    public void GuardConcurrentEdit(Product product)
    {
        var entry = Db.Entry(product);
        if (entry.State == EntityState.Unchanged) entry.State = EntityState.Modified;
    }
}
