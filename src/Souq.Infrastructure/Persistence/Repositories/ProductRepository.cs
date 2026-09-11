using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مستودع المنتجات لجهة الكتابة فقط؛ القراءات في CatalogQueries/InventoryQueries.
public class ProductRepository : RepositoryBase<Product>, IProductRepository
{
    public ProductRepository(AppDbContext db) : base(db) { }

    // نشمل المعطّلة (لا فلتر IsActive): المفتاح الأجنبي قائم بغضّ النظر عنه.
    public async Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default)
        => await Db.Products.AnyAsync(p => p.CategoryId == categoryId, ct);
}
