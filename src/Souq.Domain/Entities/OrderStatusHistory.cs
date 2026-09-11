using Souq.Domain.Common;
using Souq.Domain.Enums;

namespace Souq.Domain.Entities;

// ============================================================================
// OrderStatusHistory — سطر في سجلّ انتقالات حالة الطلب. جزء من تجمّع الطلب
// (Aggregate)، بنفس روح OrderItem: internal ctor لأن لا أحد يُنشئ سطر تاريخ
// إلا الطلب نفسه (عبر RecordStatusChange) — فيستحيل تسجيل حالة لم يمرّ بها
// الطلب فعلياً، أو نسيان تسجيلها عند إضافة انتقال جديد مستقبلاً.
//
// لا خاصية "ChangedAt" منفصلة: BaseEntity.CreatedAt (يُملأ تلقائياً عند الحفظ،
// انظر AppDbContext.SaveChangesAsync) يمثّلها تماماً — نفس المبدأ المتّبع مع
// StockMovement، فلا تكرار لمنطق الطابع الزمني.
// ============================================================================
public class OrderStatusHistory : Entity, ITenantOwned
{
    public int TenantId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Note { get; private set; }

    private OrderStatusHistory() { }

    internal OrderStatusHistory(OrderStatus status, string? note)
    {
        Status = status;
        Note = string.IsNullOrWhiteSpace(note) ? null : note;
    }
}
