using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Coupons.Queries;

public record CouponDto(
    int Id, string Code, string Type, decimal Value,
    decimal? MinOrderAmount, DateTime? ExpiresAt, int? MaxUses, int UsedCount, bool IsActive);

// كل الكوبونات (نشطة ومعطّلة) لشاشة الإدارة — مرقّمة، الأحدث أولاً.
public record GetCouponsQuery(int Page = 1, int PageSize = 20) : IRequest<PaginatedList<CouponDto>>, IPagedQuery;

public class GetCouponsHandler : IRequestHandler<GetCouponsQuery, PaginatedList<CouponDto>>
{
    private readonly ICouponQueries _coupons;
    public GetCouponsHandler(ICouponQueries coupons) => _coupons = coupons;

    public Task<PaginatedList<CouponDto>> Handle(GetCouponsQuery q, CancellationToken ct) =>
        _coupons.ListAsync(PageRequest.From(q), ct);
}
