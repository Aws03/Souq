using AwesomeAssertions;
using Souq.Application.Features.Analytics;
using Souq.Application.Features.Analytics.Contracts;
using Souq.Domain.Entities;

namespace Souq.Application.Tests.Analytics;

// ============================================================================
// **الالتقاط معطّلٌ حتى يُضبَط كاملاً** — وهذا هو الاختبار الذي يحرس نصَّ قرار المالك نفسه.
//
// قرار C-08 = A أذن بمعرّف زائر مُعتِم، وأبقى ثلاثة أجوبة للمالك «**قبل أن يُكتب أوّل صفّ**»:
// الأساس القانوني، ومدّة الحفظ، والإقامة. والطريقةُ الوحيدة لاحترام تلك الجملة حرفياً هي أن
// يمنع غيابُ الجواب الكتابةَ — لا أن يُشترط انتباهُ من ينشر.
//
// فلو مرّ يوماً `Enabled = true` بلا مدّةٍ أو بلا أساس، لَصار جوابٌ قانونيّ أثراً جانبياً لملفّ
// إعداد. هذه الاختبارات هي ما يُبقي ذلك مستحيلاً.
// ============================================================================
public class EventCaptureSettingsTests
{
    private static EventCaptureSettings Configured() =>
        new() { Enabled = true, RetentionDays = 90, LawfulBasis = "consent" };

    [Fact]
    public void الافتراضي_معطّل_وبلا_مدّة_وبلا_أساس_وبلا_معرّف_زائر()
    {
        var settings = new EventCaptureSettings();

        settings.Enabled.Should().BeFalse();
        settings.VisitorIdentifierEnabled.Should().BeFalse();
        settings.RetentionDays.Should().Be(0, "لا مدّة افتراضية: البحث وجد 13 و14 شهراً، وكلاهما بحثٌ لا قرار");
        settings.LawfulBasis.Should().BeEmpty();
        settings.CaptureIsConfigured.Should().BeFalse();
    }

    [Fact]
    public void مضبوطاً_كاملاً_يُفعَّل()
    {
        Configured().CaptureIsConfigured.Should().BeTrue();
    }

    [Fact]
    public void تفعيلٌ_بلا_أساس_قانوني_لا_يُعَدّ_مضبوطاً()
    {
        foreach (var basis in new[] { "", "   ", null })
        {
            var settings = Configured();
            settings.LawfulBasis = basis!;
            settings.CaptureIsConfigured.Should().BeFalse($"أساس '{basis}' ليس جواباً");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(EventCaptureSettings.MaxRetentionDays + 1)]
    public void تفعيلٌ_بمدّة_خارج_المدى_لا_يُعَدّ_مضبوطاً(int days)
    {
        var settings = Configured();
        settings.RetentionDays = days;
        settings.CaptureIsConfigured.Should().BeFalse();
    }

    [Fact]
    public void معطّلاً_لا_يُعَدّ_مضبوطاً_ولو_كانت_بقيّة_القيم_كاملة()
    {
        var settings = Configured();
        settings.Enabled = false;
        settings.CaptureIsConfigured.Should().BeFalse("«معطّل» جوابٌ صريح لا نصفَ إعداد");
    }

    // خمولُ ثلاثين دقيقة هو العُرف الذي التقت عليه منصّتان مستقلّتان (ADR-0050 §4). قابلٌ للضبط
    // لأنّ التعريف تجاريّ — والقيمة الافتراضية مثبّتة هنا كي لا تتغيّر بلا قصد.
    [Fact]
    public void خمول_الجلسة_ثلاثون_دقيقة_افتراضاً()
    {
        new EventCaptureSettings().SessionIdle.Should().Be(TimeSpan.FromMinutes(30));
    }
}

// ============================================================================
// سجلُّ الحمولات: كلُّ اسمٍ له نوعٌ وإصدار، **وكلُّ اسمٍ في المجال له مدخل**.
//
// آخرُ اختبارٍ هنا هو الحرس الحقيقي: اسمٌ يُضاف إلى `BehaviouralEventNames` بلا حمولةٍ مسجَّلة
// يجعل `Record` تُسقط كلَّ حدثٍ يحمله — ويُعَدّ الإسقاط، لكنّ أحداً لن ينظر إلى العدّاد قبل أن
// يفتقد اللوحةُ رقماً. والفشل عند البناء أرخص.
// ============================================================================
public class BehaviouralEventPayloadsTests
{
    [Fact]
    public void كل_اسم_في_المجال_له_حمولة_مسجَّلة()
    {
        var registered = BehaviouralEventPayloads.Names.ToHashSet(StringComparer.Ordinal);

        BehaviouralEventNames.All.Where(name => !registered.Contains(name)).Should()
            .BeEmpty("اسمٌ بلا حمولة يُسقِط كلَّ حدثٍ يحمله بصمت");
        registered.Where(name => !BehaviouralEventNames.Contains(name)).Should()
            .BeEmpty("حمولةٌ لاسمٍ لا يعرفه المجال لا تُكتب أبداً");
    }

    [Fact]
    public void الإصدار_يُحلّ_بالاسم_والنوع_معاً()
    {
        var payload = new ItemViewedPayload(1, null, 10m, "JOD", true);

        BehaviouralEventPayloads.VersionFor(BehaviouralEventNames.ItemViewed, payload).Should().Be(1);
        BehaviouralEventPayloads.VersionFor(BehaviouralEventNames.CartAdded, payload).Should()
            .BeNull("حمولةٌ لا تخصّ هذا الاسم تُسقَط، لا تُكتب تحت اسمٍ آخر");
        BehaviouralEventPayloads.VersionFor("search.performed", payload).Should().BeNull();
    }

    // المال `decimal` لا `double`: لو دخلت الفلوسُ حسابَ الفاصلة العائمة الثنائية لاختلفت تحليلاتُ
    // التاجر عن طلباته هو — وهي ثلاث خانات في الدينار.
    [Fact]
    public void المال_يُسلسَل_بقيمته_بلا_تقريب()
    {
        var json = BehaviouralEventPayloads.Serialize(
            new ItemViewedPayload(1, null, 59.955m, "JOD", true));

        json.Should().Contain("59.955");

        var back = (ItemViewedPayload)BehaviouralEventPayloads.Deserialize(BehaviouralEventNames.ItemViewed, json)!;
        back.UnitPrice.Should().Be(59.955m);
    }

    [Fact]
    public void الحقول_الفارغة_لا_تُسلسَل_فلا_تستهلك_حدّ_الحمولة()
    {
        var json = BehaviouralEventPayloads.Serialize(new ItemViewedPayload(1, null, 10m, "JOD", true));

        json.Should().NotContain("variantId").And.NotContain("null");
    }
}
