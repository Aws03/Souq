using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Coupons.Queries;

// معاينة خصم كوبون قبل الدفع (بلا أي تعديل على شيء) — تُستخدم في صفحة السلة/
// الدفع لعرض قيمة الخصم فوراً. CreateOrderHandler يعيد التحقّق من الكوبون
// مستقلاً عند الدفع الفعلي (لا نثق بمعاينة سابقة قد يكون الكوبون تغيّر بعدها).
// لا عملة في الاستعلام: المبلغ بعملة المتجر دائماً (كانت عملة يرسلها العميل — Phase 2).
public record ApplyCouponQuery(string Code, decimal Subtotal) : IRequest<Result<CouponPreviewDto>>;

public record CouponPreviewDto(string Code, decimal DiscountAmount, decimal NewTotal);

public class ApplyCouponHandler : IRequestHandler<ApplyCouponQuery, Result<CouponPreviewDto>>
{
    private readonly ICouponRepository _coupons;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public ApplyCouponHandler(ICouponRepository coupons, ITenantContext tenant, TimeProvider clock)
    {
        _coupons = coupons; _tenant = tenant; _clock = clock;
    }

    public async Task<Result<CouponPreviewDto>> Handle(ApplyCouponQuery q, CancellationToken ct)
    {
        var coupon = await _coupons.GetByCodeAsync(q.Code, ct);
        if (coupon is null)
            return Result<CouponPreviewDto>.Failure(Error.BusinessRule("CouponNotFound", "رمز الكوبون غير صحيح"));

        // مبلغ غير صالح (InvalidMoneyException) أو كوبون غير قابل للاستخدام
        // (InvalidCouponException) ⇒ استثناء مجال يُترجم مركزياً إلى 422 برمزه.
        var subtotal = new Money(q.Subtotal, _tenant.RequireTenant().Currency);
        coupon.EnsureUsable(subtotal, _clock.GetUtcNow().UtcDateTime);
        var discount = coupon.CalculateDiscount(subtotal);
        return Result<CouponPreviewDto>.Success(
            new CouponPreviewDto(coupon.Code, discount.Amount, subtotal.Amount - discount.Amount));
    }
}
