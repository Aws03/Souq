using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Coupons.Commands;

// حذف فعلي قبل أي استخدام فقط (المرحلة 10): كوبون له استخدامات (سجلّات أو عدّاد) يبقى لتقاريرها ويُعطَّل بدل حذفه —
// 409 CouponInUse برسالة تقول ذلك. الطلبات نفسها تحمل لقطة نصّية من الرمز.
public record DeleteCouponCommand(int Id) : IRequest<Result>;

public class DeleteCouponHandler : IRequestHandler<DeleteCouponCommand, Result>
{
    private readonly ICouponRepository _coupons;
    private readonly ICouponRedemptionRepository _redemptions;
    private readonly IUnitOfWork _uow;

    public DeleteCouponHandler(ICouponRepository coupons, ICouponRedemptionRepository redemptions, IUnitOfWork uow)
    {
        _coupons = coupons; _redemptions = redemptions; _uow = uow;
    }

    public async Task<Result> Handle(DeleteCouponCommand cmd, CancellationToken ct)
    {
        var coupon = await _coupons.GetByIdAsync(cmd.Id, ct);
        if (coupon is null)
            return Result.Failure(Error.NotFound("الكوبون غير موجود"));

        if (coupon.UsedCount > 0 || await _redemptions.AnyForCouponAsync(coupon.Id, ct))
            return Result.Failure(Error.Conflict("CouponInUse", "استُخدم هذا الكوبون في طلبات — عطّله بدل حذفه كي تبقى سجلّاته"));

        _coupons.Remove(coupon);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
