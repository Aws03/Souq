using Souq.Application.Common.Models;

namespace Souq.Application.Features.Coupons.Queries;

// منفذ القراءة لوحدة Promotions (ADR-0008): قائمة الكوبونات للإدارة، الأحدث أولاً.
public interface ICouponQueries
{
    Task<PaginatedList<CouponDto>> ListAsync(PageRequest page, CancellationToken ct);
}
