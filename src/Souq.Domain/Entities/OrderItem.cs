using Souq.Domain.Common;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// OrderItem — سطر داخل الطلب. هذا جزء من "تجمّع" الطلب (Aggregate).
// ملاحظة مهمة جداً: نخزّن ProductName و UnitPrice هنا بشكل "مكرّر" رغم وجودهما
// في Product. لماذا؟ لأن سعر المنتج قد يتغيّر غداً، لكن الفاتورة يجب أن تبقى
// مجمّدة كما كانت لحظة الشراء. هذا ليس تكراراً خاطئاً — بل قرار تجاري مقصود.
// (نفس المبدأ الذي شرحناه في ملف الـ Roadmap تحت "التحرّر الواعي من التطبيع".)
// ============================================================================
public class OrderItem : Entity, ITenantOwned
{
    // المستأجر على الأبناء أيضاً (لا على الجذر فقط): تصدير متجر أو نقله بـ WHERE TenantId واحد،
    // وأي استعلام مباشر على الأسطر مُرشَّح هو الآخر (MultiTenancy.md §6).
    public int TenantId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = default!;   // لقطة مجمّدة للاسم
    public Money UnitPrice { get; private set; } = default!;        // لقطة مجمّدة للسعر
    public int Quantity { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity);        // محسوبة، لا مخزّنة

    private OrderItem() { }

    internal OrderItem(int productId, string productName, Money unitPrice, int quantity)
    {
        // internal: لا يُنشأ سطر طلب إلا من داخل الطلب نفسه. هذا يحمي التجمّع.
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void IncreaseQuantity(int amount) => Quantity += amount;
}
