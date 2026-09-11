using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للكوبونات. قائمة الإدارة عبر ICouponQueries (ADR-0008).
public interface ICouponRepository : IRepository<Coupon>
{
    // الرمز فريد وغير حسّاس لحالة الأحرف (يُخزَّن دائماً بأحرف كبيرة في الكيان).
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default);
}
