using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Coupons.Commands;

// لا يُعدَّل الرمز نفسه هنا عمداً (انظر تعليق Coupon.UpdateDetails). PUT يستبدل المعطيات كلها، ومنها النافذة وحدّ العميل.
public record UpdateCouponCommand(
    int Id, DiscountType Type, decimal Value,
    decimal? MinOrderAmount, DateTime? ExpiresAt, int? MaxUses, bool IsActive,
    DateTime? StartsAt = null, int? MaxUsesPerCustomer = null
) : IRequest<Result>;

public class UpdateCouponHandler : IRequestHandler<UpdateCouponCommand, Result>
{
    private readonly ICouponRepository _coupons;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public UpdateCouponHandler(ICouponRepository coupons, ITenantContext tenant, IUnitOfWork uow)
    {
        _coupons = coupons; _tenant = tenant; _uow = uow;
    }

    public async Task<Result> Handle(UpdateCouponCommand cmd, CancellationToken ct)
    {
        var coupon = await _coupons.GetByIdAsync(cmd.Id, ct);
        if (coupon is null)
            return Result.Failure(Error.NotFound("الكوبون غير موجود"));

        coupon.UpdateDetails(cmd.Type, cmd.Value,
            cmd.MinOrderAmount.HasValue ? new Money(cmd.MinOrderAmount.Value, _tenant.RequireTenant().Currency) : null,
            cmd.ExpiresAt, cmd.MaxUses, cmd.StartsAt, cmd.MaxUsesPerCustomer);

        if (cmd.IsActive) coupon.Activate(); else coupon.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
