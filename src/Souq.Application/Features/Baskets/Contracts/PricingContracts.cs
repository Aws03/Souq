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
public sealed record PricedLine(
    int ProductId, int VariantId, string Name, IReadOnlyDictionary<string, string> Names, string? ImageUrl,
    Money UnitPrice, int Quantity, Money LineTotal, bool Sellable,
    string? VariantLabel = null, string? Sku = null, bool VariantRequired = false,
    IReadOnlyDictionary<string, string>? VariantLabels = null);

// الكوبون كما قُيِّم: مطبَّق، أو مرفوض برمز الخطأ ورسالته (ModuleDisabled، CouponNotFound، InvalidCoupon).
public sealed record CouponOutcome(string Code, bool Applied, string? ErrorCode, string? Message);

// الشحن كما قُيِّم: الطرق المتاحة للعنوان، والمختارة، وهل يلزم اختيار (للمتجر طرق)، أو مشكلة برمزها — ShippingMethodRequired
// (لم تُختر)، ShippingMethodUnavailable (المختارة لا تخدم العنوان)، ShippingNotAvailable (لا طريقة تخدمه).
public sealed record ShippingOutcome(
    IReadOnlyList<ShippingOption> Options, ShippingOption? Selected, bool Required, string? ErrorCode, string? Message);

public sealed record PriceQuote(
    string Currency, IReadOnlyList<PricedLine> Lines, Money Subtotal, CouponOutcome? Coupon, Money Discount,
    Money Shipping, Money Tax, Money Total, ShippingOutcome? ShippingOutcome = null);
