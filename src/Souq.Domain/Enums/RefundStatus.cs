namespace Souq.Domain.Enums;

// حالة استرداد (المرحلة 11). Pending: طُلب ولم تُعرف نتيجته من البوّابة بعد (انقطاع أثناء الطلب) — يُعاد بالمفتاح نفسه.
public enum RefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}
