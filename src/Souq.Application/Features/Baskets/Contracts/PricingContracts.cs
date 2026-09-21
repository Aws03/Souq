using Souq.Application.Features.Shipping.Contracts;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Baskets.Contracts;

// ============================================================================
// خطّ التسعير الواحد (المرحلة 8، Modules.md: Shopping يملك IPricing ويشاركه الدفع): الفرعي ← الخصم ← الشحن ← الضريبة
// ← الإجمالي، من أسعار الكتالوج الحيّة بعملة المتجر. السلة تُعرض به والطلب يُنشأ به، فإجمالي السلة هو إجمالي الدفع
// بالبناء لا بالمصادفة. مشكلة الكوبون نتيجة في العرض لا فشل له: السلة تبقى معروضة، والدفع يرفضها برمزها.
// customerId (المرحلة 10): عميل معروف ⇒ حدّ استخدامه للكوبون يُحتسب؛ زائر ⇒ لا (الدفع للعملاء وحدهم ويعيد التحقّق).
// shipping (المرحلة 12): الطريقة المختارة ودولة العنوان؛ مشكلة الشحن نتيجة كالكوبون تماماً.
// ============================================================================
public interface IPricing
{
    Task<PriceQuote> QuoteAsync(
        IReadOnlyList<PricingLine> lines, string? couponCode, int? customerId, ShippingRequest? shipping, CancellationToken ct);
}

// VariantId: المتغيّر المطلوب بعينه (سطر سلة، أو سطر طلب سمّى متغيّراً). null ⇒ متغيّر المنتج الضمني — الوحيد النشط؛
// لمنتج بأكثر من متغيّر نشط لا يُفترض شيء (VariantRequired). المتغيّر يُقبل من منتج السطر نفسه فقط.
public sealed record PricingLine(int ProductId, int Quantity, int? VariantId = null);

// طلب الشحن: الطريقة المختارة (null: لم تُختر) ودولة العنوان برمز ISO (null: غير معروفة ⇒ الطرق غير المقيَّدة بدول).
public sealed record ShippingRequest(int? MethodId, string? Country);

// سطر مسعَّر، بترتيب أسطر الطلب نفسه. Sellable=false: المنتج لم يعد منشوراً أو ليس في هذا المتجر، أو المتغيّر معطّل أو لا
// يخصّ المنتج ⇒ خارج المجموع، والدفع يرفضه. VariantRequired: السطر لم يسمِّ متغيّراً ولمنتجه أكثر من متغيّر نشط. Names
// بكل لغات المنتج للعرض، وName بلغة المتجر الافتراضية للقطة الطلب. VariantLabel وSku لقطتا المتغيّر للطلب (الوصف null
// حتى خيارات المتغيّرات — V2).
// VariantLabel: لقطة وصف المتغيّر بلغة المتجر الافتراضية (تُجمَّد على سطر الطلب). VariantLabels: الوصف بكل لغة يعرفها
// المنتج، للعرض الحيّ بلغة الزائر — كما Names لاسم المنتج (V3).
// ============================================================================
// **UnitCost سرٌّ تجاري، وهو الحقل الوحيد هنا الذي لا يراه المتسوّق أبداً** (C11). يسافر مع
// السطر لسبب واحد: لقطةُ تكلفةٍ تُجمَّد على سطر الطلب، تماماً كما تُجمَّد `UnitPrice` و`Sku`
// و`VariantLabel` عبر المسار نفسه — وبلا التجميد يتحرّك ربحُ العام الماضي كلّما صُحِّح رقم اليوم.
//
// ولأنّه الأول من نوعه في هذا العقد، يحرسه اختبار: `WhiteLabelSourceTests` يفشل إن ظهر حقل
// تكلفة في أيّ استجابة تصل متجراً. الخريطة إلى `BasketLineDto` تُسقطه، وهذا مقصود لا سهو.
// ============================================================================
public sealed record PricedLine(
    int ProductId, int VariantId, string Name, IReadOnlyDictionary<string, string> Names, string? ImageUrl,
    Money UnitPrice, int Quantity, Money LineTotal, bool Sellable,
    string? VariantLabel = null, string? Sku = null, bool VariantRequired = false,
    IReadOnlyDictionary<string, string>? VariantLabels = null,
    Money? UnitCost = null);

// الكوبون كما قُيِّم: مطبَّق، أو مرفوض برمز الخطأ ورسالته (ModuleDisabled، CouponNotFound، InvalidCoupon).
public sealed record CouponOutcome(string Code, bool Applied, string? ErrorCode, string? Message);

// الشحن كما قُيِّم: الطرق المتاحة للعنوان، والمختارة، وهل يلزم اختيار (للمتجر طرق)، أو مشكلة برمزها — ShippingMethodRequired
// (لم تُختر)، ShippingMethodUnavailable (المختارة لا تخدم العنوان)، ShippingNotAvailable (لا طريقة تخدمه).
public sealed record ShippingOutcome(
    IReadOnlyList<ShippingOption> Options, ShippingOption? Selected, bool Required, string? ErrorCode, string? Message);

// ============================================================================
// TaxSnapshot وTaxReason (ADR-0055): لقطةُ القواعد التي أنتجت الضريبة، **وسببُ صفرِها إن كان
// صفراً**. اللقطةُ تُجمَّد على الطلب عند التثبيت، والسببُ يُعرَض للتاجر لا للمتسوّق: صفرٌ بلا سببٍ
// يقرأ كأنه عطب، فيُفتَح له بلاغٌ بدل أن يُكمَل إعداد.
//
// null للقطة ⇒ لم تُجمَع ضريبة، وهو حال كل متجرٍ لم يختر ملفّ اختصاصٍ متحقَّقاً منه.
// ============================================================================
public sealed record PriceQuote(
    string Currency, IReadOnlyList<PricedLine> Lines, Money Subtotal, CouponOutcome? Coupon, Money Discount,
    Money Shipping, Money Tax, Money Total, ShippingOutcome? ShippingOutcome = null,
    Souq.Domain.ValueObjects.TaxSnapshot? TaxSnapshot = null, string? TaxReason = null);
