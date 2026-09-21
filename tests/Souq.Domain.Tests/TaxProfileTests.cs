using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// ملفُّ ضريبةِ اختصاصٍ وإصداراتُه (ADR-0055، قرار المالك P-06).
//
// **أهمّ ما يُختبر هنا هو ما لا يمكن الوصول إليه**: `Verified` بلا اسمِ متحقِّق، وتعديلُ إصدارٍ
// منشور، وجمعُ ضريبةٍ بإصدارٍ لم يؤكّده أحد. فقرار المالك يقول إنّ قيمَ الملفّ **إعدادٌ يحتاج
// تحقّقاً مهنياً قبل الاستخدام التجاري**، وهذه الاختبارات هي ما يجعل تلك الجملة قاعدةً في الكود
// لا نصيحةً في وثيقة.
// ============================================================================
public class TaxProfileTests
{
    private static readonly DateTime Jan = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jul = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TaxProfile Profile() => new("JO", "Jordan — general");

    private static TaxProfileVersion Published(TaxProfile profile, DateTime from, int basisPoints = 1600)
    {
        var version = profile.AddDraft(from, TaxPriceMode.Inclusive, shippingTaxable: true);
        version.SetRates([new TaxRate("standard", "Standard rate", basisPoints)]);
        version.Publish();
        return version;
    }

    [Fact]
    public void الملفّ_يُطبّع_رمز_اختصاصه_ويرفض_ما_ليس_رمزاً()
    {
        new TaxProfile(" jo ", "Jordan").Jurisdiction.Should().Be("JO");

        // ورمزٌ بأرقام **مقبول**: أقاليم ISO 3166-2 تحمل أرقاماً (FR-01).
        new TaxProfile("FR-01", "Ain").Jurisdiction.Should().Be("FR-01");

        foreach (var bad in new[] { "J", "", "JO_X", "JO X", new string('J', 11) })
            ((Action)(() => new TaxProfile(bad, "x"))).Should().Throw<InvalidTaxProfileException>($"رمز '{bad}' مرفوض");
    }

    // ============================================================================
    // **الإصدار المنشور مجمَّد.** وهذا ما يجعل ضريبةَ العام الماضي معروفةً من القاعدة: لو أمكن
    // تعديلُ نسبةٍ منشورة لَتغيّرت قواعدُ مدّةٍ صدرت فواتيرُها.
    // ============================================================================
    [Fact]
    public void الإصدار_المنشور_لا_يُعدَّل_بأي_وجه()
    {
        var profile = Profile();
        var version = Published(profile, Jan);

        ((Action)(() => version.SetRates([new TaxRate("standard", "x", 500)])))
            .Should().Throw<InvalidTaxProfileException>();
        ((Action)(() => version.SetPriceMode(TaxPriceMode.Exclusive))).Should().Throw<InvalidTaxProfileException>();
        ((Action)(() => version.SetShippingTaxable(false))).Should().Throw<InvalidTaxProfileException>();
        ((Action)(() => version.SetThreshold(new Money(30_000m, "JOD")))).Should().Throw<InvalidTaxProfileException>();
        ((Action)(() => version.SetNotes("x"))).Should().Throw<InvalidTaxProfileException>();
        ((Action)(() => version.Publish())).Should().Throw<InvalidTaxProfileException>();
    }

    // التصحيحُ إصدارٌ **بتاريخ نفاذٍ لاحق**: إصدارٌ ينفذ قبل منشورٍ قائم يُغيّر الماضي بأثر رجعيّ.
    [Fact]
    public void التصحيح_إصدار_جديد_ولا_ينفذ_قبل_آخر_منشور()
    {
        var profile = Profile();
        Published(profile, Jul);

        ((Action)(() => profile.AddDraft(Jan, TaxPriceMode.Inclusive, true)))
            .Should().Throw<InvalidTaxProfileException>("تاريخ نفاذٍ يسبق منشوراً يغيّر مدّةً صدرت فواتيرُها");

        var next = profile.AddDraft(Jul.AddDays(1), TaxPriceMode.Inclusive, true);
        next.Version.Should().Be(2);
    }

    [Fact]
    public void مسوّدة_واحدة_في_كل_وقت()
    {
        var profile = Profile();
        profile.AddDraft(Jan, TaxPriceMode.Inclusive, true);

        ((Action)(() => profile.AddDraft(Jul, TaxPriceMode.Inclusive, true)))
            .Should().Throw<InvalidTaxProfileException>("مسوّدتان تجعلان «الإصدار التالي» سؤالاً بلا جواب");
    }

    [Fact]
    public void لا_يُنشر_إصدار_بلا_نسبة()
    {
        var draft = Profile().AddDraft(Jan, TaxPriceMode.Exclusive, false);

        ((Action)(() => draft.Publish())).Should()
            .Throw<InvalidTaxProfileException>("إصدارٌ لا يُحتسب منه شيء يبدو صالحاً ولا يفعل شيئاً");
    }

    // ============================================================================
    // **لا جمعَ إلا بمنشورٍ ومُتحقَّقٍ منه** — وهي القاعدة التي يخدمها كلُّ ما في الملفّ.
    // ============================================================================
    [Fact]
    public void لا_تُجمَع_ضريبة_إلا_بإصدار_منشور_ومتحقَّق_منه()
    {
        var profile = Profile();
        var draft = profile.AddDraft(Jan, TaxPriceMode.Inclusive, true);
        draft.SetRates([new TaxRate("standard", "Standard", 1600)]);

        draft.AllowsCollection.Should().BeFalse("مسوّدة");
        draft.Verification.State.Should().Be(TaxVerificationState.Unverified, "تبدأ غير متحقَّق منها دائماً");

        // ولا يُتحقَّق من مسوّدة: التحقّق من قيمٍ قد تتغيّر قبل النشر لا يعني شيئاً.
        ((Action)(() => draft.Verify("مكتب محاسبة", Jan, null))).Should().Throw<InvalidTaxProfileException>();

        draft.Publish();
        draft.AllowsCollection.Should().BeFalse("منشورٌ ولم يتحقّق منه أحد — وهذا هو الحدّ الذي يوجد لأجله");

        draft.Verify("مكتب محاسبة", Jul, "مراجعة 2026");
        draft.AllowsCollection.Should().BeTrue();
        draft.Verification.By.Should().Be("مكتب محاسبة");
        draft.Verification.At.Should().Be(Jul);
    }

    // التحقّق فعلُ إنسان يُسمّي نفسه: بلا اسمٍ لا تُوجد حالةُ `Verified` أصلاً.
    [Fact]
    public void التحقّق_يلزمه_اسم_متحقِّق()
    {
        var profile = Profile();
        var version = Published(profile, Jan);

        foreach (var bad in new[] { "", "   ", null })
            ((Action)(() => version.Verify(bad!, Jan, null))).Should().Throw<InvalidTaxProfileException>();

        version.Verification.State.Should().Be(TaxVerificationState.Unverified);
    }

    // سحبُ التحقّق يُوقف الجمع فوراً ولا يمسّ الإصدار: الطلباتُ التي احتُسبت به تحمل لقطاتها.
    [Fact]
    public void سحب_التحقّق_يُوقف_الجمع_ولا_يحذف_شيئاً()
    {
        var profile = Profile();
        var version = Published(profile, Jan);
        version.Verify("محاسب", Jan, null);

        version.RequireConfirmation("تغيّرت التعليمات");

        version.AllowsCollection.Should().BeFalse();
        version.Verification.State.Should().Be(TaxVerificationState.RequiresProfessionalConfirmation);
        version.Status.Should().Be(TaxProfileVersionStatus.Published, "الإصدار يبقى — تاريخُه لا يتغيّر");
        version.Rates.Should().HaveCount(1);
    }

    [Fact]
    public void نقاط_الأساس_بين_صفر_والمئة_بالمئة()
    {
        new TaxRate("z", "Zero", 0).BasisPoints.Should().Be(0);
        new TaxRate("h", "Hundred", TaxRate.MaxBasisPoints).BasisPoints.Should().Be(10_000);

        foreach (var bad in new[] { -1, TaxRate.MaxBasisPoints + 1 })
            ((Action)(() => new TaxRate("x", "X", bad))).Should().Throw<InvalidTaxProfileException>();
    }

    // ============================================================================
    // نسبُ الفئة **تُجمَع**: بعض الاختصاصات تفرض ضريبتَين على المبيعة نفسها. والجمعُ صريحٌ كي لا
    // يُفترض أنّ الأولى هي الوحيدة — وهو افتراضٌ يُنتج رقماً أقلّ بلا أن يُخطئ أحد.
    // ============================================================================
    [Fact]
    public void نسب_الفئة_تُجمَع_وفئة_بلا_نسبة_صفر()
    {
        var draft = Profile().AddDraft(Jan, TaxPriceMode.Exclusive, false);
        draft.SetRates([
            new TaxRate("national", "National", 1000),
            new TaxRate("municipal", "Municipal", 200),
            new TaxRate("books", "Books", 0, "books"),
        ]);

        draft.BasisPointsFor(TaxRate.DefaultCategory).Should().Be(1200, "ضريبتان على المبيعة نفسها");
        draft.BasisPointsFor("books").Should().Be(0);
        draft.BasisPointsFor("unknown").Should().Be(0, "فئةٌ لا نسبةَ لها ليست نقصاً في الإعداد");
        draft.RatesFor(TaxRate.DefaultCategory).Should().HaveCount(2);
    }

    [Fact]
    public void رمز_واحد_لكل_نسبة()
    {
        var draft = Profile().AddDraft(Jan, TaxPriceMode.Exclusive, false);

        ((Action)(() => draft.SetRates([new TaxRate("s", "A", 100), new TaxRate("s", "B", 200)])))
            .Should().Throw<InvalidTaxProfileException>();
    }

    // ============================================================================
    // الإصدارُ النافذ في لحظة: أحدثُ منشورٍ لا يتجاوزها. والمسوّدةُ لا تنفذ على أحدٍ ولو كان
    // تاريخُها في الماضي — وهذا ما يجعل «اكتب القواعد الآن وانشرها لاحقاً» آمناً.
    // ============================================================================
    [Fact]
    public void الإصدار_النافذ_أحدث_منشور_لا_يتجاوز_اللحظة()
    {
        var profile = Profile();
        var first = Published(profile, Jan, 1000);
        var second = Published(profile, Jul, 1600);
        var futureDraft = profile.AddDraft(Jul.AddMonths(6), TaxPriceMode.Inclusive, true);
        futureDraft.SetRates([new TaxRate("standard", "Standard", 2000)]);

        profile.VersionOn(Jan.AddDays(-1)).Should().BeNull("لا قاعدةَ قبل أوّل نفاذ");
        profile.VersionOn(Jan).Should().Be(first);
        profile.VersionOn(Jul.AddDays(-1)).Should().Be(first);
        profile.VersionOn(Jul).Should().Be(second);
        profile.VersionOn(Jul.AddYears(1)).Should().Be(second, "المسوّدة لا تنفذ ولو مضى تاريخها");
    }
}
