using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Coupons.Queries;

// UsedCount (المرحلة 10): الاستخدامات المحجوزة لطلبات لم تُدفع بعد والمؤكَّدة بالدفع.
public record CouponDto(
    int Id, string Code, string Type, decimal Value, decimal? MinOrderAmount, DateTime? StartsAt, DateTime? ExpiresAt,
    int? MaxUses, int? MaxUsesPerCustomer, int UsedCount, bool IsActive);

// كل الكوبونات (نشطة ومعطّلة) لشاشة الإدارة — مرقّمة، الأحدث أولاً.
public record GetCouponsQuery(int Page = 1, int PageSize = 20) : IRequest<PaginatedList<CouponDto>>, IPagedQuery;

public class GetCouponsHandler : IRequestHandler<GetCouponsQuery, PaginatedList<CouponDto>>
{
    private readonly ICouponQueries _coupons;
    public GetCouponsHandler(ICouponQueries coupons) => _coupons = coupons;

    public Task<PaginatedList<CouponDto>> Handle(GetCouponsQuery q, CancellationToken ct) =>
        _coupons.ListAsync(PageRequest.From(q), ct);
}

// استخدامات كوبون (المرحلة 10): أيّ طلب ولأيّ عميل وبأيّ خصم وحالته — الأحدث أولاً. كوبون متجر آخر ⇒ 404.
public record CouponRedemptionDto(
    int OrderId, int OrderNumber, int CustomerId, string? CustomerName, decimal Discount, string Currency, string Status,
    DateTime CreatedAt);

public record GetCouponRedemptionsQuery(int CouponId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PaginatedList<CouponRedemptionDto>>>, IPagedQuery;

public class GetCouponRedemptionsQueryValidator : PagedQueryValidator<GetCouponRedemptionsQuery>;

public class GetCouponRedemptionsHandler : IRequestHandler<GetCouponRedemptionsQuery, Result<PaginatedList<CouponRedemptionDto>>>
{
    private readonly ICouponQueries _coupons;
    public GetCouponRedemptionsHandler(ICouponQueries coupons) => _coupons = coupons;

    public async Task<Result<PaginatedList<CouponRedemptionDto>>> Handle(GetCouponRedemptionsQuery q, CancellationToken ct) =>
        await _coupons.ListRedemptionsAsync(q.CouponId, PageRequest.From(q), ct) is { } page
            ? Result<PaginatedList<CouponRedemptionDto>>.Success(page)
            : Result<PaginatedList<CouponRedemptionDto>>.Failure(Error.NotFound("الكوبون غير موجود"));
}
