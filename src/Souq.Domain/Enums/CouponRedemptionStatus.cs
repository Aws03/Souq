namespace Souq.Domain.Enums;

// حالة استخدام كوبون لطلب (المرحلة 10). الترتيب ثابت (قيم مخزّنة).
public enum CouponRedemptionStatus
{
    Reserved = 0,    // طلب أُنشئ ولم يُدفع بعد — يُحتسب من حدّ الكوبون وحدّ العميل
    Confirmed = 1,   // دُفع الطلب
    Released = 2,    // أُلغي الطلب فعاد الاستخدام للكوبون
}
