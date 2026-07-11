using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
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
        string? keyword, int? categoryId, int page, int pageSize, CancellationToken ct = default)
    {
        // نبني الاستعلام تدريجياً حسب المعايير المتوفّرة (Query Composition).
        var query = Db.Products.Include(p => p.Category).Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(p => p.Name.Contains(keyword) || p.Description.Contains(keyword));
        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        var total = await query.CountAsync(ct);   // العدد الكلي قبل الترقيم (للأزرار)

        // الترقيم الفعلي: نتخطّى صفحات سابقة ونأخذ صفحة واحدة فقط.
        var items = await query.OrderByDescending(p => p.Id)
                               .Skip((page - 1) * pageSize).Take(pageSize)
                               .ToListAsync(ct);
        return (items, total);
    }

    // نشمل المعطّلة (لا فلتر IsActive): المفتاح الأجنبي قائم بغضّ النظر عنه.
    public async Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default)
        => await Db.Products.AnyAsync(p => p.CategoryId == categoryId, ct);
}
