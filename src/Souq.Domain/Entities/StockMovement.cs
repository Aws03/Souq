using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// StockMovement — سطر في سجلّ حركة المخزون. كيان "للقراءة فقط بعد الإنشاء":
// كل تغيّر في مخزون منتج (بيع، توريد، تصحيح، إرجاع) يُسجَّل هنا مرة واحدة ولا
// يُعدَّل بعدها — سجلّ تدقيق لا يُزوَّر (مثل روح OrderItem المجمّد تماماً).
//
// QuantityChange مُوقَّع بإشارته: سالب = نقص، موجب = زيادة. NewQuantity لقطة
// لمستوى المخزون بعد هذه الحركة — تتيح عرض تسلسل المخزون في السجلّ دون إعادة
// حسابه، وتكشف أي تعارض مستقبلي بين السجلّ والمخزون الفعلي.
// ============================================================================
public class StockMovement : Entity, ITenantOwned
{
    public int TenantId { get; private set; }
    public int ProductId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int QuantityChange { get; private set; }   // مُوقَّع: سالب نقص، موجب زيادة
    public int NewQuantity { get; private set; }       // مستوى المخزون بعد الحركة
    public string? Note { get; private set; }

    private StockMovement() { }

    public StockMovement(int productId, StockMovementType type, int quantityChange,
                         int newQuantity, string? note = null)
    {
        if (quantityChange == 0)
            throw new InvalidProductDataException("حركة المخزون يجب أن تغيّر الكمية (لا صفر)");

        ProductId = productId;
        Type = type;
        QuantityChange = quantityChange;
        NewQuantity = newQuantity;
        Note = string.IsNullOrWhiteSpace(note) ? null : note;
    }

    // مُنشئ مساعد يستنتج NewQuantity من مستوى المخزون بعد تطبيق التغيير على
    // المنتج — يجعل موقع الاستدعاء أوضح (يمرّر المنتج بعد تعديله فقط).
    public static StockMovement For(Product product, StockMovementType type,
                                    int quantityChange, string? note = null)
        => new(product.Id, type, quantityChange, product.StockQuantity, note);
}
