using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Coupons.Commands;

// حذف فعلي: الطلبات لا تحمل مفتاحاً أجنبياً للكوبون، بل لقطة نصّية من رمزه
// (CouponCode) — فحذف الكوبون لا يفسد أي طلب تاريخي، بخلاف الفئة/المنتج.
public record DeleteCouponCommand(int Id) : IRequest<Result>;

public class DeleteCouponHandler : IRequestHandler<DeleteCouponCommand, Result>
{
    private readonly ICouponRepository _coupons;
    private readonly IUnitOfWork _uow;

    public DeleteCouponHandler(ICouponRepository coupons, IUnitOfWork uow)
    {
        _coupons = coupons; _uow = uow;
    }

    public async Task<Result> Handle(DeleteCouponCommand cmd, CancellationToken ct)
    {
        var coupon = await _coupons.GetByIdAsync(cmd.Id, ct);
        if (coupon is null)
            return Result.Failure("الكوبون غير موجود", "NotFound");

        _coupons.Remove(coupon);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
