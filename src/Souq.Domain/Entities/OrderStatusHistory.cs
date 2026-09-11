using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// OrderStatusHistory — سطر في سجلّ انتقالات حالة الطلب. جزء من تجمّع الطلب
// (Aggregate)، بنفس روح OrderItem: internal ctor لأن لا أحد يُنشئ سطر تاريخ
// إلا الطلب نفسه (عبر RecordStatusChange) — فيستحيل تسجيل حالة لم يمرّ بها
// الطلب فعلياً، أو نسيان تسجيلها عند إضافة انتقال جديد مستقبلاً.
//
// لا خاصية "ChangedAt" منفصلة: BaseEntity.CreatedAt (يُملأ تلقائياً عند الحفظ،
// انظر AppDbContext.SaveChangesAsync) يمثّلها تماماً — نفس المبدأ المتّبع مع
// StockMovement، فلا تكرار لمنطق الطابع الزمني. ومن غيّرها (المرحلة 9): النوع
// ومعرّف الحساب للموظّف أو العميل.
// ============================================================================
public class OrderStatusHistory : Entity, ITenantOwned
{
    public int TenantId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Note { get; private set; }
    public OrderActorKind ChangedBy { get; private set; }
    public int? ChangedByUserId { get; private set; }

    private OrderStatusHistory() { }

    internal OrderStatusHistory(OrderStatus status, string? note, OrderActor by)
    {
        Status = status;
        Note = string.IsNullOrWhiteSpace(note) ? null : note;
        ChangedBy = by.Kind;
        ChangedByUserId = by.UserId;
    }
}
