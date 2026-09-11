using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ كتابة استخدامات الكوبونات (المرحلة 10).
public interface ICouponRedemptionRepository : IRepository<CouponRedemption>
{
    Task<CouponRedemption?> GetForOrderAsync(int orderId, CancellationToken ct = default);

    // استخدامات العميل الفعّالة (محجوزة أو مؤكَّدة) لكوبون — لحدّ العميل.
    Task<int> CountActiveAsync(int couponId, int customerId, CancellationToken ct = default);

    Task<bool> AnyForCouponAsync(int couponId, CancellationToken ct = default);

    // ينسى الكوبونات والاستخدامات المحمَّلة بعد تعارض — المحاولة التالية تقرأ من جديد.
    void Reset();
}
