using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للتقييمات. قائمة تقييمات المنتج ومتوسطها عبر IReviewQueries (ADR-0008).
public interface IReviewRepository : IRepository<Review>
{
    // عميل واحد لا يُقيّم نفس المنتج أكثر من مرة (بغضّ النظر عن عدد مرات شرائه).
    Task<bool> HasCustomerReviewedProductAsync(int customerId, int productId, CancellationToken ct = default);
}
