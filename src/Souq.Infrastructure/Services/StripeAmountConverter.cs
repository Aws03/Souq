using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Services;

// ============================================================================
// تحويل Money إلى "أصغر وحدة" كما يتوقّعها Stripe (رقم صحيح بلا فواصل). حسب وثائق
// Stripe (docs.stripe.com/currencies، روجعت 2026-09-11): كل العملات ثنائية الخانات
// ما لم تُدرَج كعديمة الخانات — حتى ISK/UGX تُمثَّل ×100 للتوافق الخلفي.
//
// ⚠️ **P-05 — قرار مالك مفتوح.** الدينار الأردني (3 خانات ISO) غير مذكور في وثائق Stripe كحالة خاصة، فيُعامَل
// اليوم ثنائياً (×100 بتقريب تجاري لأقرب 0.01). إن عامل حسابُ المالك الحقيقي الدينار كعملة ثلاثية الخانات
// فالمضاعف 1000، والخطأ يعني **تحصيل عُشر الثمن**. لا يُحسَم هذا إلا بعملية شحن حقيقية على الحساب الحقيقي.
//
// ولذلك جُهِّز المسار هنا في M6 بدل انتظار اللحظة: `HonoursIsoDecimals` هو القرار كلّه في سطر واحد.
//   • false (اليوم) — كل ما ليس عديم الخانات ×100، أياً كانت خاناته في ISO.
//   • true — المضاعف من CurrencyInfo (مصدر الحقيقة الواحد لخانات العملات)، فيصير الدينار ×1000 تلقائياً.
// الاختباران في StripeAmountConverterTests يثبتان **الفرضيتين معاً** عبر الحمل الثاني أدناه، فالقلب لاحقاً
// تغييرُ قيمةٍ واحدة أمام اختبار أخضر أصلاً، لا كتابةُ منطق وتقريبٍ تحت ضغط اكتشافٍ في الإنتاج.
//
// ولماذا لا تُكتب العملات ثلاثية الخانات هنا كقائمة؟ لأنّ WhiteLabelSourceTests يمنع كتابة رمز عملة المتجر
// حرفياً في src/ — ومصدر الخانات هو CurrencyInfo وحده، فهذا الملف يقرأ منه ولا ينسخه. (وقد أمسك هذا
// الاختبارُ النسخةَ الأولى من هذا التعليق نفسه، إذ كتبت الرمز مثالاً.)
// ============================================================================
public static class StripeAmountConverter
{
    // استثناء Stripe الصريح: هذه ×1 مهما قالت ISO. ليست رموز عملة "علامة تجارية" بل جدول مزوّد الدفع.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    // ============================================================================
    // **هذا السطر هو جواب P-05.** static readonly لا const: مع const يطوي المترجم الفرع الآخر فيصير كوداً
    // غير قابل للوصول (وتحذيراً يُعامَل خطأً في هذا المستودع)، فلا يبقى الفرعان مبنيَّين معاً.
    // تغييره إلى true يحتاج ADR: هو تغيير سلوك مال صريح (AGENTS.md §0 قاعدة 3).
    // ============================================================================
    public static readonly bool HonoursIsoDecimals = false;

    public static long ToMinorUnits(Money amount) => ToMinorUnits(amount, HonoursIsoDecimals);

    // ============================================================================
    // الحمل الصريح: يسمح للاختبار بإثبات الفرضية غير المفعَّلة بلا تغيير ما يُرسَل فعلاً اليوم.
    //
    // `Math.Max(100, …)` ليس احتياطاً بل تصحيحٌ مقصود: ISK وUGX خاناتهما صفر في ISO لكنّ Stripe يريدهما ×100
    // للتوافق الخلفي. اشتقاق ساذج بـ 10^خانات كان سينقلهما إلى ×1 بصمت — أي تحصيل جزء من المئة من الثمن.
    // وعديمة الخانات عند Stripe تُفحص أولاً، فلا يبلغها هذا الحدّ الأدنى أصلاً.
    // ============================================================================
    public static long ToMinorUnits(Money amount, bool honoursIsoDecimals)
    {
        var factor = ZeroDecimalCurrencies.Contains(amount.Currency)
            ? 1m
            : honoursIsoDecimals
                ? Math.Max(100m, Pow10(CurrencyInfo.MinorUnits(amount.Currency)))
                : 100m;

        return (long)decimal.Round(amount.Amount * factor, 0, MidpointRounding.AwayFromZero);
    }

    private static decimal Pow10(int exponent)
    {
        var value = 1m;
        for (var i = 0; i < exponent; i++) value *= 10m;
        return value;
    }
}
