using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Coupons.Queries;

public record CouponDto(
    int Id, string Code, string Type, decimal Value,
    decimal? MinOrderAmount, DateTime? ExpiresAt, int? MaxUses, int UsedCount, bool IsActive);

public record GetCouponsQuery(int Page = 1, int PageSize = 20) : IRequest<PaginatedList<CouponDto>>;

public class GetCouponsHandler : IRequestHandler<GetCouponsQuery, PaginatedList<CouponDto>>
{
    private readonly ICouponRepository _coupons;
    public GetCouponsHandler(ICouponRepository coupons) => _coupons = coupons;

    public async Task<PaginatedList<CouponDto>> Handle(GetCouponsQuery q, CancellationToken ct)
    {
        var (items, total) = await _coupons.GetPagedAsync(q.Page, q.PageSize, ct);

        var dtos = items.Select(c => new CouponDto(
            c.Id, c.Code, c.Type.ToString(), c.Value,
            c.MinOrderAmount?.Amount, c.ExpiresAt, c.MaxUses, c.UsedCount, c.IsActive)).ToList();

        return new PaginatedList<CouponDto>(dtos, total, q.Page, q.PageSize);
    }
}
