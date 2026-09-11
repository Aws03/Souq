using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Coupons.Commands;

// لا يُعدَّل الرمز نفسه هنا عمداً (انظر تعليق Coupon.UpdateDetails).
public record UpdateCouponCommand(
    int Id, DiscountType Type, decimal Value,
    decimal? MinOrderAmount, DateTime? ExpiresAt, int? MaxUses, bool IsActive
) : IRequest<Result>;

public class UpdateCouponHandler : IRequestHandler<UpdateCouponCommand, Result>
{
    private readonly ICouponRepository _coupons;
    private readonly IUnitOfWork _uow;

    public UpdateCouponHandler(ICouponRepository coupons, IUnitOfWork uow)
    {
        _coupons = coupons; _uow = uow;
    }

    public async Task<Result> Handle(UpdateCouponCommand cmd, CancellationToken ct)
    {
        var coupon = await _coupons.GetByIdAsync(cmd.Id, ct);
        if (coupon is null)
            return Result.Failure(Error.NotFound("الكوبون غير موجود"));

        coupon.UpdateDetails(cmd.Type, cmd.Value,
            cmd.MinOrderAmount.HasValue ? new Money(cmd.MinOrderAmount.Value) : null,
            cmd.ExpiresAt, cmd.MaxUses);

        if (cmd.IsActive) coupon.Activate(); else coupon.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
