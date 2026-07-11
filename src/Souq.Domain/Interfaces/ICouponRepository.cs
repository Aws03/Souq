using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

public interface ICouponRepository : IRepository<Coupon>
{
    // الرمز فريد وغير حسّاس لحالة الأحرف (يُخزَّن دائماً بأحرف كبيرة في الكيان).
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<(IReadOnlyList<Coupon> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default);
}
