using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// Notification — إشعار داخل التطبيق لحساب واحد في المتجر (المرحلة 14، ADR-0034): للعميل عن طلبه، وللإدارة عن طلب جديد أو
// مخزون ينفد. النصّ لا يُخزَّن: النوع ومعاملات صغيرة (رقم الطلب، الحالة، اسم المنتج) تعرضها الواجهة بلغة الزائر. المستلم معرّف
// حساب بلا مفتاح أجنبي: الحسابات لا تُحذف أبداً (تُعطَّل أو تُمحى بياناتها)، وTenantId الحساب اختياري فلا مفتاح مركّب إليه.
// ============================================================================
public class Notification : Entity, ITenantOwned
{
    public const int KindMaxLength = 40;
    public const int DataMaxLength = 1000;

    public int TenantId { get; private set; }
    public int RecipientUserId { get; private set; }
    public string Kind { get; private set; } = default!;
    public string Data { get; private set; } = default!;
    public DateTime? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    private Notification() { }

    public Notification(int recipientUserId, string kind, string data)
    {
        if (recipientUserId <= 0)
            throw new InvalidNotificationException("مستلم الإشعار حساب محفوظ");
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > KindMaxLength)
            throw new InvalidNotificationException("نوع الإشعار غير صالح");
        if (data is null || data.Length > DataMaxLength)
            throw new InvalidNotificationException($"بيانات الإشعار حتى {DataMaxLength} حرف");

        RecipientUserId = recipientUserId;
        Kind = kind;
        Data = data;
    }

    // مرّة واحدة: قراءة ثانية لا تغيّر وقت القراءة الأولى.
    public void MarkRead(DateTime now) => ReadAt ??= now;
}

// أنواع الإشعارات — عقد ثابت مع الواجهة (نصوصها في ملفات الترجمة).
public static class NotificationKinds
{
    public const string OrderStatus = "order.status";   // للعميل: دُفع/شُحن/سُلِّم/أُلغي طلبه
    public const string NewOrder = "order.new";         // للإدارة: طلب جديد مدفوع
    public const string LowStock = "stock.low";         // للإدارة: متاح منتج نزل عن حدّ التنبيه
    // C6 (ADR-0058): لصاحب المتجر — فاتورةُ اشتراكٍ استحقّت ولم تُسدَّد.
    public const string InvoiceOverdue = "billing.invoice.overdue";
}
