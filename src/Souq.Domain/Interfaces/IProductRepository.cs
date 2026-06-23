using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// واجهة متخصّصة للمنتجات: ترث العام وتضيف استعلامات خاصة بالمنتجات.
// نضيف فقط ما يحتاجه المجال فعلاً (لا نخمّن المستقبل).
public interface IProductRepository : IRepository<Product>
{
    Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId, CancellationToken ct = default);
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? keyword, int? categoryId, int page, int pageSize, CancellationToken ct = default);
}
