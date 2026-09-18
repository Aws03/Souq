using AwesomeAssertions;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Services;

namespace Souq.IntegrationTests;

// ============================================================================
// تحويل المبلغ لأصغر وحدة يتوقّعها Stripe — خطأ هنا يعني تحصيل مبلغ مختلف عن الطلب.
// (اختبار وحدة خالص؛ يعيش هنا لأنه المشروع الوحيد الذي يرى طبقة Infrastructure.)
//
// **الفرضيتان معاً (M6، تحضير P-05).** الدينار ثلاثي الخانات في ISO، ووثائق Stripe لا تذكره كحالة خاصة —
// فالمُفعَّل اليوم هو ×100، ولا يُحسَم الأمر إلا بعملية شحن حقيقية على حساب المالك (§5 من SouqMasterPlan.md).
// الملف يثبت **السلوكين**: المُفعَّل، والذي سيصير مفعَّلاً إن عاد الجواب "ثلاثية". فإن حان القرار يكون
// المطلوب قلبَ `HonoursIsoDecimals` وحدها أمام اختبار أخضر أصلاً — لا كتابةَ منطقٍ وتقريبٍ تحت الضغط.
// ============================================================================
public class StripeAmountConverterTests
{
    [Theory]
    [InlineData(10.99, "USD", 1099)]
    [InlineData(500, "JPY", 500)]      // عديمة الخانات عند Stripe: كما هي
    [InlineData(5, "ISK", 500)]        // استثناء Stripe: ×100 للتوافق الخلفي رغم أنّ ISO يقول صفر خانات
    [InlineData(59.9, "JOD", 5990)]    // الدينار يُعامَل ثنائياً حسب الوثائق (P-05 للتحقّق)
    [InlineData(59.955, "JOD", 5996)]  // تقريب تجاري لأقرب 0.01
    public void المُفعَّل_اليوم_يحوّل_لأصغر_وحدة_حسب_وثائق_Stripe(decimal amount, string currency, long expected)
    {
        StripeAmountConverter.ToMinorUnits(new Money(amount, currency)).Should().Be(expected);
    }

    [Fact]
    public void المُفعَّل_اليوم_هو_الفرضية_الثنائية_صراحةً()
    {
        // يُقال بصوت عالٍ كي لا يُقلَب السلوك سهواً: قلبه قرار مال يحتاج ADR (AGENTS.md §0 قاعدة 3).
        StripeAmountConverter.HonoursIsoDecimals.Should().BeFalse(
            "P-05 لم يُحسَم بعد — المُفعَّل هو ×100 للدينار حتى تُجرى عملية شحن حقيقية على حساب المالك");
    }

    [Theory]
    // لو عاد جواب P-05 "الدينار ثلاثي الخانات": الفلس هو الوحدة الصغرى ⇒ ×1000، بلا أي تقريب يضيع فلساً.
    [InlineData(59.9, "JOD", 59900)]
    [InlineData(59.955, "JOD", 59955)]
    [InlineData(0.001, "JOD", 1)]
    // وبقيّة العملات لا تتحرّك: هذا هو نصف الاختبار المهمّ — القلب يجب ألّا يمسّ غير ثلاثيات الخانات.
    [InlineData(10.99, "USD", 1099)]
    [InlineData(500, "JPY", 500)]
    // ISK وUGX صفر خانات في ISO لكنّ Stripe يريدهما ×100: اشتقاق ساذج بـ 10^خانات كان سينقلهما إلى ×1
    // (تحصيل جزء من المئة من الثمن). الحدّ الأدنى 100 يمنع ذلك، وهذا هو الاختبار الذي يحرسه.
    [InlineData(5, "ISK", 500)]
    [InlineData(5, "UGX", 500)]
    public void الفرضية_الثلاثية_جاهزة_ومختبَرة_قبل_تفعيلها(decimal amount, string currency, long expected)
    {
        StripeAmountConverter.ToMinorUnits(new Money(amount, currency), honoursIsoDecimals: true)
            .Should().Be(expected);
    }

    [Fact]
    public void الفرضيتان_تختلفان_على_ثلاثيات_الخانات_وحدها()
    {
        // الدينار: عُشر الثمن أو عشرة أضعافه — وهو بالضبط ما يجعل P-05 قراراً لا تخميناً.
        var dinar = new Money(59.9m, "JOD");
        StripeAmountConverter.ToMinorUnits(dinar, honoursIsoDecimals: false).Should().Be(5990);
        StripeAmountConverter.ToMinorUnits(dinar, honoursIsoDecimals: true).Should().Be(59900);

        // وما ليس ثلاثي الخانات لا يتغيّر إطلاقاً بين الفرضيتين.
        foreach (var code in new[] { "USD", "EUR", "JPY", "ISK", "UGX", "SAR" })
        {
            var money = new Money(12m, code);
            StripeAmountConverter.ToMinorUnits(money, honoursIsoDecimals: true)
                .Should().Be(StripeAmountConverter.ToMinorUnits(money, honoursIsoDecimals: false),
                    $"قلب P-05 يجب ألّا يمسّ {code}");
        }
    }
}
