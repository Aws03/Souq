using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Services;

// ============================================================================
// تحويل Money إلى "أصغر وحدة" كما يتوقّعها Stripe (رقم صحيح بلا فواصل). حسب وثائق
// Stripe (docs.stripe.com/currencies، روجعت 2026-09-11): كل العملات ثنائية الخانات
// ما لم تُدرَج كعديمة الخانات — حتى ISK/UGX تُمثَّل ×100 للتوافق الخلفي.
//
// ⚠️ الدينار الأردني (3 خانات ISO) غير مذكور في الوثائق كحالة خاصة ⇒ يُعامَل ثنائياً
// (×100 مع تقريب تجاري لأقرب 0.01)، وهو السلوك السابق نفسه الآن صريحاً ومُختبَراً.
// يجب التأكّد من Stripe قبل تفعيل الدفع الحقيقي بالدينار: إن عامل حسابُك الدينار
// كعملة ثلاثية الخانات فالمضاعف 1000 — والخطأ هنا يعني تحصيل عُشر الثمن (P-05).
// ============================================================================
public static class StripeAmountConverter
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    public static long ToMinorUnits(Money amount)
    {
        var factor = ZeroDecimalCurrencies.Contains(amount.Currency) ? 1m : 100m;
        return (long)decimal.Round(amount.Amount * factor, 0, MidpointRounding.AwayFromZero);
    }
}
