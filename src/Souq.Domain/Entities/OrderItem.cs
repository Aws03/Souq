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
    public const int ProductNameMaxLength = 200;
    public const int VariantLabelMaxLength = 200;

    public int ProductId { get; private set; }

    // المتغيّر المشترى بعينه (الوحدة القابلة للبيع، D-21) — مرجع لا لقطة: المتغيّر لا يُحذف أبداً. الأسطر التي سبقت
    // هذا العمود رُبطت بالمتغيّر الافتراضي لمنتجها، وهو يقيناً ما بيع (منتج بمتغيّر واحد طوال عمره — ADR-0039).
    public int VariantId { get; private set; }
    public string ProductName { get; private set; } = default!;   // لقطة مجمّدة للاسم

    // لقطتا المتغيّر لحظة الشراء (وصفه بلغة المتجر الافتراضية، وSKU): SKU قد يتغيّر غداً والفاتورة لا. null = لم يُسجَّل —
    // منتج بلا خيارات لا وصف لمتغيّره، والأسطر التاريخية لا تُملأ بقيم اليوم (اختلاق لبيانات لم تُحفظ).
    public string? VariantLabel { get; private set; }
    public string? Sku { get; private set; }
    public Money UnitPrice { get; private set; } = default!;        // لقطة مجمّدة للسعر
    public int Quantity { get; private set; }

    // ============================================================================
    // لقطة **التكلفة** لحظة الشراء (C11) — بالمبدأ نفسه الذي يجمّد السعر والاسم أعلاه، ولسبب
    // أقوى: بلا هذه اللقطة كان الهامش يُحسب من تكلفة المتغيّر **الحالية**، فيتحرّك ربحُ العام
    // الماضي كلّما صحّح التاجر رقمَ تكلفةٍ اليوم. تقريرٌ مالي يتغيّر بأثر رجعي ليس تقريراً.
    //
    // و`null` تعني "لم تُعرف لحظة البيع" ولا تُملأ بقيمة اليوم أبداً — كما لا يُملأ `VariantLabel`
    // للأسطر التاريخية. الأسطر السابقة لهذا العمود تبقى فارغة، والتقرير يقول كم من إيراده مغطّى
    // بتكلفة معروفة بدل أن يخترع الباقي.
    // ============================================================================
    // عمود مبلغ واحد والعملة من `UnitPrice` — نفس شكل `ProductVariant.Cost`، ولنفس السبب: عمود
    // عملةٍ ثانٍ على السطر يفتح احتمال تناقضه مع عملة سعره.
    private decimal? _unitCostAmount;
    public Money? UnitCost => _unitCostAmount is decimal amount ? new Money(amount, UnitPrice.Currency) : null;

    public Money LineTotal => UnitPrice.Multiply(Quantity);        // محسوبة، لا مخزّنة

    private OrderItem() { }

    internal OrderItem(
        int productId, int variantId, string productName, string? variantLabel, string? sku,
        Money unitPrice, int quantity, Money? unitCost = null)
    {
        // internal: لا يُنشأ سطر طلب إلا من داخل الطلب نفسه. هذا يحمي التجمّع.
        ProductId = productId;
        VariantId = variantId;
        ProductName = productName;
        VariantLabel = variantLabel;
        Sku = sku;
        UnitPrice = unitPrice;
        Quantity = quantity;
        _unitCostAmount = unitCost?.Amount;
    }

    internal void IncreaseQuantity(int amount) => Quantity += amount;
}
