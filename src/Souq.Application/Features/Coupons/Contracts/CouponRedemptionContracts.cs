using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Coupons.Contracts;

// ============================================================================
// استخدامات الكوبونات (المرحلة 10، وحدة Promotions، ADR-0030): Ordering يحجز استخداماً عند إنشاء الطلب — داخل معاملته،
// والقواعد كلها (النافذة، الحدّ العام، حدّ العميل، الحدّ الأدنى) تُعاد على قراءة جديدة، وتعارض rowversion يُعاد — ثم يؤكّده
// بالدفع ويحرّره بأي إلغاء. التأكيد بلا حفظ (وحدة عمل المستدعي تحفظه مع الطلب)؛ الحجز والتحرير يحفظان بإعادة المحاولة.
// ============================================================================
public interface ICouponRedemptions
{
    Task ReserveAsync(string couponCode, int orderId, int customerId, Money subtotal, Money discount, CancellationToken ct);

    Task ConfirmAsync(int orderId, CancellationToken ct);

    Task ReleaseAsync(int orderId, CancellationToken ct);
}
