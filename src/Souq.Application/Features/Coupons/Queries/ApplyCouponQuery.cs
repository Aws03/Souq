using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Coupons.Queries;

// معاينة خصم كوبون قبل الدفع (بلا أي تعديل على شيء) — تُستخدم في صفحة السلة/
// الدفع لعرض قيمة الخصم فوراً. CreateOrderHandler يعيد التحقّق من الكوبون
// مستقلاً عند الدفع الفعلي (لا نثق بمعاينة سابقة قد يكون الكوبون تغيّر بعدها).
public record ApplyCouponQuery(string Code, decimal Subtotal, string Currency) : IRequest<Result<CouponPreviewDto>>;

public record CouponPreviewDto(string Code, decimal DiscountAmount, decimal NewTotal);

public class ApplyCouponHandler : IRequestHandler<ApplyCouponQuery, Result<CouponPreviewDto>>
{
    private readonly ICouponRepository _coupons;
    public ApplyCouponHandler(ICouponRepository coupons) => _coupons = coupons;

    public async Task<Result<CouponPreviewDto>> Handle(ApplyCouponQuery q, CancellationToken ct)
    {
        var coupon = await _coupons.GetByCodeAsync(q.Code, ct);
        if (coupon is null)
            return Result<CouponPreviewDto>.Failure("رمز الكوبون غير صحيح", "CouponNotFound");

        var subtotal = new Money(q.Subtotal, q.Currency);
        try { coupon.EnsureUsable(subtotal, DateTime.UtcNow); }
        catch (InvalidCouponException ex) { return Result<CouponPreviewDto>.Failure(ex.Message, "InvalidCoupon"); }

        var discount = coupon.CalculateDiscount(subtotal);
        return Result<CouponPreviewDto>.Success(
            new CouponPreviewDto(coupon.Code, discount.Amount, subtotal.Amount - discount.Amount));
    }
}
