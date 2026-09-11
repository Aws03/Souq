using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// StockMovement — سطر في سجلّ حركة المخزون: كيان "للقراءة فقط بعد الإنشاء" — كل تغيّر في الموجود (توريد، بيع،
// تصحيح، إرجاع، إلغاء بيع) يُسجَّل مرّة واحدة ولا يُعدَّل بعدها.
//
// المرحلة 6: لا يُنشأ إلا من InventoryItem بعد تطبيق التغيير عليه (مُنشئ internal) — فلا سطر بلا تغيير حقيقي
// ولا تغيير بلا سطر، وΣ QuantityChange لمخزون = موجوده. QuantityChange مُوقَّع (سالب نقص، موجب زيادة)،
// وNewQuantity لقطة الموجود بعد الحركة. ProductId للعرض بالمنتج؛ InventoryItemId هو المخزون الذي تغيّر.
// ============================================================================
public class StockMovement : Entity, ITenantOwned
{
    public const int NoteMaxLength = 300;

    public int TenantId { get; private set; }
    public int ProductId { get; private set; }
    public int InventoryItemId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int QuantityChange { get; private set; }
    public int NewQuantity { get; private set; }
    public string? Note { get; private set; }

    private StockMovement() { }

    internal StockMovement(InventoryItem item, StockMovementType type, int quantityChange, string? note)
    {
        if (quantityChange == 0)
            throw new InvalidInventoryOperationException("حركة المخزون يجب أن تغيّر الكمية (لا صفر)");

        ProductId = item.ProductId;
        InventoryItemId = item.Id;
        Type = type;
        QuantityChange = quantityChange;
        NewQuantity = item.OnHand;
        var trimmed = note?.Trim();
        // ملاحظات النظام قد تحمل نص بوّابة الدفع — تُقصّ لحدّ العمود بدل فشل حفظ حركة حقيقية.
        Note = string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, NoteMaxLength)];
    }
}
