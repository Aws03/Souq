using Souq.Domain.Enums;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// جدول انتقالات حالة الطلب (المرحلة 9) — المصدر الوحيد لما يجوز. كل دالة انتقال في Order تمرّ به، وواجهة الإدارة تقرأ
// منه إجراءاتها المتاحة (لا نسخة ثانية في JavaScript). العميل يلغي طلبه قبل الدفع فقط؛ إلغاء المدفوع قرار المتجر
// (الاسترداد في المرحلة 11). لا انتقال يعيد طلباً إلى Pending، ولا خروج من Delivered أو Cancelled.
// ============================================================================
public static class OrderTransitions
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed = new Dictionary<OrderStatus, OrderStatus[]>
    {
        [OrderStatus.Pending] = [OrderStatus.Paid, OrderStatus.Cancelled],
        [OrderStatus.Paid] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static IReadOnlyList<OrderStatus> TargetsFrom(OrderStatus from) => Allowed[from];

    public static bool CanMove(OrderStatus from, OrderStatus to, OrderActor by) =>
        Allowed[from].Contains(to)
        && (by.Kind != OrderActorKind.Customer || (from == OrderStatus.Pending && to == OrderStatus.Cancelled));

    public static bool CustomerCanCancel(OrderStatus status) => CanMove(status, OrderStatus.Cancelled, OrderActor.Customer());
}
