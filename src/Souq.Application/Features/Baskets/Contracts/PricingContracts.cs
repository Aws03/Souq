using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Baskets.Contracts;

// ============================================================================
// خطّ التسعير الواحد (المرحلة 8، Modules.md: Shopping يملك IPricing ويشاركه الدفع): الفرعي ← الخصم ← الشحن ← الضريبة
// ← الإجمالي، من أسعار الكتالوج الحيّة بعملة المتجر. السلة تُعرض به والطلب يُنشأ به، فإجمالي السلة هو إجمالي الدفع
// بالبناء لا بالمصادفة. مشكلة الكوبون نتيجة في العرض لا فشل له: السلة تبقى معروضة، والدفع يرفضها برمزها.
// ============================================================================
public interface IPricing
{
    Task<PriceQuote> QuoteAsync(IReadOnlyList<PricingLine> lines, string? couponCode, CancellationToken ct);
}

public sealed record PricingLine(int ProductId, int Quantity);

// سطر مسعَّر، بترتيب أسطر الطلب نفسه. Sellable=false: المنتج لم يعد منشوراً أو ليس في هذا المتجر ⇒ خارج المجموع،
// والدفع يرفضه. Names بكل لغات المنتج للعرض، وName بلغة المتجر الافتراضية للقطة الطلب.
public sealed record PricedLine(
    int ProductId, int VariantId, string Name, IReadOnlyDictionary<string, string> Names, string? ImageUrl,
    Money UnitPrice, int Quantity, Money LineTotal, bool Sellable);

// الكوبون كما قُيِّم: مطبَّق، أو مرفوض برمز الخطأ ورسالته (ModuleDisabled، CouponNotFound، InvalidCoupon).
public sealed record CouponOutcome(string Code, bool Applied, string? ErrorCode, string? Message);

public sealed record PriceQuote(
    string Currency, IReadOnlyList<PricedLine> Lines, Money Subtotal, CouponOutcome? Coupon, Money Discount,
    Money Shipping, Money Tax, Money Total);
