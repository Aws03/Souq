using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

public interface IReviewRepository : IRepository<Review>
{
    // تقييمات منتج مرقّمة + متوسط التقييم (يُحسَب في الاستعلام نفسه — لا يُخزَّن
    // على المنتج كي لا يفسد تزامنه مع التقييمات الفعلية).
    Task<(IReadOnlyList<Review> Items, int TotalCount, double AverageRating)> GetByProductAsync(
        int productId, int page, int pageSize, CancellationToken ct = default);

    // عميل واحد لا يُقيّم نفس المنتج أكثر من مرة (بغضّ النظر عن عدد مرات شرائه).
    Task<bool> HasCustomerReviewedProductAsync(int customerId, int productId, CancellationToken ct = default);
}
