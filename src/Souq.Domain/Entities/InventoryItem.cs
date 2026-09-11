using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// InventoryItem — مخزون وحدة قابلة للبيع (متغيّر منتج) في وحدة Inventory (المرحلة 6، ADR-0026). جذر صغير ساخن
// التزامن (rowversion): الموجود (OnHand)، والمحجوز لطلبات لم تُدفع بعد (Reserved)، والمتاح للبيع = الفرق.
//   • المتاح لا يصبح سالباً: لا حجز بلا توفّر، ولا تصحيح ينزل بالموجود تحت المحجوز.
//   • كل تغيّر في الموجود يُنتج سطر سجلّ واحداً بالضبط، والسطر لا يُنشأ إلا هنا ⇒ Σ الحركات = OnHand دائماً.
//   • الحجز سجلّ صريح (StockReservation) لا أثر جانبي: يُلتزم عند الدفع (بيع)، ويُحرَّر عند الإلغاء أو انتهاء
//     المهلة، ويُعاد للموجود إن أُلغي طلب مدفوع — كل انتقال مرّة واحدة (مضمون التكرار) ⇒ Σ النشط = Reserved.
// ============================================================================
public class InventoryItem : Entity, ITenantOwned
{
    public const int DefaultLowStockThreshold = 5;
    public const int ReasonMaxLength = 200;

    public int TenantId { get; private set; }
    public int ProductId { get; private set; }
    public int VariantId { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int LowStockThreshold { get; private set; } = DefaultLowStockThreshold;

    public int Available => OnHand - Reserved;

    // التنبيه على المتاح لا الموجود: المحجوز لطلبات قائمة لم يعد قابلاً للبيع.
    public bool IsLowStock => Available <= LowStockThreshold;

    private InventoryItem() { }

    public InventoryItem(int productId, int variantId, int lowStockThreshold = DefaultLowStockThreshold)
    {
        if (productId <= 0 || variantId <= 0)
            throw new InvalidInventoryOperationException("المخزون يخصّ متغيّراً محفوظاً لمنتج");
        ProductId = productId;
        VariantId = variantId;
        SetLowStockThreshold(lowStockThreshold);
    }

    // استلام وحدات: توريد، إرجاع عميل، أو عودة بيع طلب ملغى (عبر Restock).
    public StockMovement Receive(int quantity, StockMovementType type, string? note = null)
    {
        if (quantity <= 0)
            throw new InvalidInventoryOperationException("الكمية المستلمة يجب أن تكون أكبر من صفر");
        if (type is not (StockMovementType.Purchase or StockMovementType.Return))
            throw new InvalidInventoryOperationException($"{type} ليست حركة استلام");
        OnHand += quantity;
        return Record(type, quantity, note);
    }

    // تصحيح بفارق وسبب (جرد، تلف، فقد) — يحلّ محلّ تعيين المخزون المطلق (Phase 0 C4): الفارق يُطبَّق على القيمة
    // الحالية، فبيع حدث بين فتح النموذج وحفظه لا يُمحى.
    public StockMovement Adjust(int delta, string reason)
    {
        var trimmed = reason?.Trim() ?? "";
        if (delta == 0)
            throw new InvalidInventoryOperationException("التصحيح يجب أن يغيّر الكمية");
        if (trimmed.Length is 0 or > ReasonMaxLength)
            throw new InvalidInventoryOperationException($"سبب التصحيح مطلوب (حتى {ReasonMaxLength} حرفاً)");
        if (OnHand + delta < Reserved)
            throw new InvalidInventoryOperationException(
                $"لا يمكن إنزال الموجود ({OnHand}) إلى {OnHand + delta}: المحجوز لطلبات قائمة {Reserved}");
        OnHand += delta;
        return Record(StockMovementType.Adjustment, delta, trimmed);
    }

    // حجز لطلب لم يُدفع: المتاح ينقص والموجود لا يتغيّر (لا سطر سجلّ — الحجز نفسه هو السجلّ).
    public StockReservation Reserve(string reference, int quantity, DateTime expiresAt, string displayName)
    {
        if (quantity <= 0)
            throw new InvalidInventoryOperationException("كمية الحجز يجب أن تكون أكبر من صفر");
        if (quantity > Available)
            throw new InsufficientStockException(displayName, quantity, Math.Max(Available, 0));
        Reserved += quantity;
        return new StockReservation(this, reference, quantity, expiresAt);
    }

    // الالتزام عند الدفع: المحجوز يخرج من الموجود، وهنا وحده يُسجَّل البيع. حجز غير نشط ⇒ لا شيء (تكرار).
    public StockMovement? Commit(StockReservation reservation, DateTime now)
    {
        EnsureOwns(reservation);
        if (!reservation.IsActive) return null;
        Reserved -= reservation.Quantity;
        OnHand -= reservation.Quantity;
        reservation.Close(ReservationStatus.Committed, now);
        return Record(StockMovementType.Sale, -reservation.Quantity, reservation.Reference);
    }

    // تحرير حجز لم يُلتزم (إلغاء قبل الدفع، فشل الدفع، انتهاء المهلة): المتاح يعود بلا سطر سجلّ.
    public bool Release(StockReservation reservation, DateTime now, bool expired = false)
    {
        EnsureOwns(reservation);
        if (!reservation.IsActive) return false;
        Reserved -= reservation.Quantity;
        reservation.Close(expired ? ReservationStatus.Expired : ReservationStatus.Released, now);
        return true;
    }

    // طلب مدفوع أُلغي قبل شحنه: ما بيع يعود للموجود بسطر إلغاء. حجز غير ملتزم ⇒ لا شيء.
    public StockMovement? Restock(StockReservation reservation, DateTime now, string? note)
    {
        EnsureOwns(reservation);
        if (reservation.Status != ReservationStatus.Committed) return null;
        OnHand += reservation.Quantity;
        reservation.Close(ReservationStatus.Restocked, now);
        return Record(StockMovementType.Cancellation, reservation.Quantity, note ?? reservation.Reference);
    }

    public void SetLowStockThreshold(int threshold)
    {
        if (threshold < 0)
            throw new InvalidInventoryOperationException("حدّ التنبيه لا يكون سالباً");
        LowStockThreshold = threshold;
    }

    private void EnsureOwns(StockReservation reservation)
    {
        if (reservation.InventoryItemId != Id)
            throw new InvalidInventoryOperationException("الحجز لا يخصّ هذا المخزون");
    }

    private StockMovement Record(StockMovementType type, int change, string? note) => new(this, type, change, note);
}

// ============================================================================
// StockReservation — حجز صريح لكمية من مخزون لمرجع خارجي (طلب: "order:{id}"). وحدة Inventory لا تعرف الطلبات:
// تعرف المرجع فقط (Modules.md). يُنشأ عبر InventoryItem.Reserve وحده، وتُغلقه دوال المخزون نفسه.
// ============================================================================
public class StockReservation : Entity, ITenantOwned
{
    public const int ReferenceMaxLength = 64;

    public int TenantId { get; private set; }
    public int InventoryItemId { get; private set; }
    public string Reference { get; private set; } = default!;
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    public bool IsActive => Status == ReservationStatus.Active;

    private StockReservation() { }

    internal StockReservation(InventoryItem item, string reference, int quantity, DateTime expiresAt)
    {
        var trimmed = reference?.Trim() ?? "";
        if (trimmed.Length is 0 or > ReferenceMaxLength)
            throw new InvalidInventoryOperationException($"مرجع الحجز مطلوب (حتى {ReferenceMaxLength} حرفاً)");
        InventoryItemId = item.Id;
        Reference = trimmed;
        Quantity = quantity;
        ExpiresAt = expiresAt;
        Status = ReservationStatus.Active;
    }

    internal void Close(ReservationStatus status, DateTime now)
    {
        Status = status;
        ClosedAt = now;
    }
}
