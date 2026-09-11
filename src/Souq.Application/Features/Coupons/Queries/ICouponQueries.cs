using Souq.Application.Common.Models;

namespace Souq.Application.Features.Coupons.Queries;

// منفذ القراءة لوحدة Promotions (ADR-0008): قائمة الكوبونات للإدارة، واستخدامات كوبون (المرحلة 10؛ null إن لم يكن في
// هذا المتجر).
public interface ICouponQueries
{
    Task<PaginatedList<CouponDto>> ListAsync(PageRequest page, CancellationToken ct);

    Task<PaginatedList<CouponRedemptionDto>?> ListRedemptionsAsync(int couponId, PageRequest page, CancellationToken ct);
}
