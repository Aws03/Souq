using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// فتراتُ الفوترة وأحداثُها، وإعدادُ فوترة المنصّة (C5، ADR-0056).
//
// **الثابتُ الذي يوجد كلُّ هذا لأجله: لا يلتحق حدثٌ بفترةٍ أُغلقت.** فاتورةُ تلك الفترة صدرت،
// وما يلتحق بها بعدها يجعل مستنداً لا يساوي ما تحته.
// ============================================================================
public class BillingPeriodTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mid = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    private static BillingPeriod Period() => new(7, Start, End);

    [Fact]
    public void الفترة_تبدأ_مفتوحة_وتُغلق_على_مرحلتين()
    {
        var period = Period();
        period.Status.Should().Be(BillingPeriodStatus.Open);
        period.AcceptsEvents.Should().BeTrue();

        period.BeginClose();
        period.Status.Should().Be(BillingPeriodStatus.Closing);
        // توقّفت عن القبول قبل أن تصير نهائية — وهذا هو سببُ وجود الحالة الوسطى.
        period.AcceptsEvents.Should().BeFalse();

        period.Close(Mid);
        period.Status.Should().Be(BillingPeriodStatus.Closed);
        period.ClosedAtUtc.Should().Be(Mid);
    }

    [Fact]
    public void لا_قفزَ_من_مفتوحة_إلى_مغلقة()
    {
        // القفزُ يعني أنّ أحداثاً كانت تُقبَل في اللحظة التي كانت أرقامُها تُجمَع فيها.
        var close = () => Period().Close(Mid);
        close.Should().Throw<InvalidBillingPeriodException>();
    }

    [Fact]
    public void بدءُ_الإغلاق_يُستأنَف_ولا_يُستأنَف_بعد_الإغلاق()
    {
        var period = Period();
        period.BeginClose();

        // استئنافُ إغلاقٍ تعثّر يمرّ من هنا — فلا ترمي على حالتها الحالية.
        period.BeginClose();
        period.Status.Should().Be(BillingPeriodStatus.Closing);

        period.Close(Mid);
        var reopen = () => period.BeginClose();
        reopen.Should().Throw<InvalidBillingPeriodException>();
    }

    [Fact]
    public void نهاية_الفترة_حصرية_فلا_تتداخل_فترتان_ولا_تسقط_لحظة()
    {
        var period = Period();
        period.Contains(Start).Should().BeTrue();
        period.Contains(End).Should().BeFalse();       // أوّلُ لحظةٍ في الفترة التالية
        period.Contains(End.AddTicks(-1)).Should().BeTrue();
    }

    [Fact]
    public void فترة_تنتهي_قبل_أن_تبدأ_مرفوضة_وفترة_بلا_متجر_مرفوضة()
    {
        var inverted = () => new BillingPeriod(7, End, Start);
        var tenantless = () => new BillingPeriod(0, Start, End);

        inverted.Should().Throw<InvalidBillingPeriodException>();
        tenantless.Should().Throw<InvalidBillingPeriodException>();
    }

    // ── الأحداث ───────────────────────────────────────────────────────────────

    [Fact]
    public void الحدث_لا_يلتحق_بفترة_توقّفت_عن_القبول()
    {
        var period = Period();
        period.BeginClose();

        var attach = () => new BillableEvent(7, "storage.gb", 5, Mid, "key-1", period);
        attach.Should().Throw<InvalidBillingPeriodException>();
    }

    [Fact]
    public void الحدث_خارج_مدّة_فترته_مرفوض_وحدث_فترة_متجر_آخر_مرفوض()
    {
        var period = Period();

        var outside = () => new BillableEvent(7, "storage.gb", 5, End.AddDays(1), "key-1", period);
        outside.Should().Throw<InvalidBillingPeriodException>();

        var crossTenant = () => new BillableEvent(8, "storage.gb", 5, Mid, "key-1", period);
        crossTenant.Should().Throw<InvalidBillingPeriodException>();
    }

    [Fact]
    public void المقياس_يُطبَّع_ويرفض_ما_ليس_اسم_مقياس()
    {
        var period = Period();
        new BillableEvent(7, " Storage.GB ", 5, Mid, "key-1", period).Meter.Should().Be("storage.gb");

        foreach (var bad in new[] { "", "storage gb", "storage_gb", "مقياس", new string('m', 61) })
        {
            var invalid = () => new BillableEvent(7, bad, 5, Mid, "key", period);
            invalid.Should().Throw<InvalidBillingPeriodException>($"المقياس «{bad}» ليس اسماً مقبولاً");
        }
    }

    [Fact]
    public void كمّية_غير_موجبة_مرفوضة_ومفتاح_عدم_التكرار_مطلوب()
    {
        var period = Period();

        var zero = () => new BillableEvent(7, "storage.gb", 0, Mid, "key", period);
        var negative = () => new BillableEvent(7, "storage.gb", -1, Mid, "key", period);
        var keyless = () => new BillableEvent(7, "storage.gb", 1, Mid, "  ", period);

        zero.Should().Throw<InvalidBillingPeriodException>();
        negative.Should().Throw<InvalidBillingPeriodException>();
        keyless.Should().Throw<InvalidBillingPeriodException>();
    }

    [Fact]
    public void الوحدة_لا_تُحمَّل_على_فاتورتين()
    {
        // **وحدةٌ في فاتورتين مالٌ يُطالَب به مرّتين.** والرمي لا التجاهل: الصمتُ هنا يجعل
        // العطبَ يُكتشف من شكوى تاجر لا من اختبار.
        var billable = new BillableEvent(7, "storage.gb", 5, Mid, "key-1", Period());
        billable.PlatformInvoiceId.Should().BeNull();

        billable.BillOn(11);
        billable.PlatformInvoiceId.Should().Be(11);

        var twice = () => billable.BillOn(12);
        twice.Should().Throw<InvalidBillingPeriodException>();
        billable.PlatformInvoiceId.Should().Be(11);
    }
}

// ============================================================================
// إعدادُ فوترة المنصّة — وفيه يعيش جوابُ المالك عن `C-15`.
//
// **والاختبارُ الأهمّ هنا هو الفشلُ المغلق**: بلا عملةٍ ومُصدِرٍ لا تُصدَر فاتورة. فالعملةُ
// **لا تُكتب في الشيفرة** (قاعدةُ الواجهة البيضاء، ويحرسها `WhiteLabelSourceTests`)، ومن ثمّ
// يجب أن يكون غيابُها حالةً يعرفها النظامُ ويقولها، لا مفاجأةً عند أوّل إصدار.
// ============================================================================
public class PlatformBillingSettingsTests
{
    [Fact]
    public void الإعداد_يبدأ_بلا_عملة_ولا_يُصدِر_شيئاً()
    {
        var settings = PlatformBillingSettings.Empty();

        settings.Currency.Should().BeNull();
        settings.IssuerName.Should().BeNull();
        settings.CanIssue.Should().BeFalse();

        // وعملةٌ وحدها لا تكفي: المستندُ يحتاج مُصدِراً باسمه.
        settings.SetCurrency("JOD");
        settings.CanIssue.Should().BeFalse();

        settings.SetIssuer("سوق", null, null);
        settings.CanIssue.Should().BeTrue();
    }

    [Fact]
    public void العملة_تُوحَّد_صيغتها_وتُفرَّغ_بالفراغ_ويُرفض_رمزٌ_غير_صالح()
    {
        var settings = PlatformBillingSettings.Empty();

        settings.SetCurrency(" jod ");
        settings.Currency.Should().Be("JOD");

        settings.SetCurrency("   ");
        settings.Currency.Should().BeNull("إفراغُها يعيد الإعدادَ إلى حالة «لم يُقرَّر»");

        var invalid = () => settings.SetCurrency("XXXX");
        invalid.Should().Throw<InvalidMoneyException>("فحصُ ISO 4217 يعيش في Money وحدها");
    }

    [Fact]
    public void بادئتا_الترقيم_لا_تتطابقان()
    {
        // وإلّا حمل مستندان مختلفان — فاتورةٌ وإشعارُ دائن — الرقمَ نفسه من سلسلتين منفصلتين.
        var settings = PlatformBillingSettings.Empty();
        var same = () => settings.SetNumberPrefixes("INV", "inv");
        same.Should().Throw<InvalidPlatformBillingSettingsException>();

        settings.SetNumberPrefixes("inv", "cn");
        settings.InvoiceNumberPrefix.Should().Be("INV");
        settings.CreditNoteNumberPrefix.Should().Be("CN");
    }

    [Fact]
    public void إلغاء_اختيار_ملفّ_الضريبة_يُطفئ_الجمع_معه()
    {
        // حالةٌ تقول «أجمع» بلا ملفّ تدّعي ما لا تفعل — القاعدةُ نفسها في `StoreTaxSettings`.
        var settings = PlatformBillingSettings.Empty();
        settings.SelectTaxProfile(4, collectionEnabled: true);
        settings.TaxCollectionEnabled.Should().BeTrue();

        settings.SelectTaxProfile(null, collectionEnabled: true);
        settings.TaxProfileId.Should().BeNull();
        settings.TaxCollectionEnabled.Should().BeFalse();
    }

    [Fact]
    public void المهل_محصورة_في_مداها()
    {
        var settings = PlatformBillingSettings.Empty();

        var negativeTerms = () => settings.SetTerms(-1, 7);
        var hugeGrace = () => settings.SetTerms(30, PlatformBillingSettings.MaxGracePeriodDays + 1);

        negativeTerms.Should().Throw<InvalidPlatformBillingSettingsException>();
        hugeGrace.Should().Throw<InvalidPlatformBillingSettingsException>();

        settings.SetTerms(30, 7);
        settings.PaymentTermsDays.Should().Be(30);
        settings.GracePeriodDays.Should().Be(7);
    }
}

// سعرُ الخطة (C5): مكانُ القيمة لا القيمة — و`C-12` ما يزال المالك.
public class PlanPricingTests
{
    private static Plan Draft() => new("growth", 1, "خطة النموّ");

    [Fact]
    public void الخطة_تبدأ_بلا_سعر_ودورتها_شهر()
    {
        var plan = Draft();
        plan.Price.Should().BeNull();
        plan.BillingIntervalMonths.Should().Be(Plan.MinBillingIntervalMonths);
    }

    [Fact]
    public void السعر_يُجمَّد_بالنشر()
    {
        var plan = Draft();
        plan.SetPrice(new Money(30, "JOD"), 1);
        plan.Publish();

        // مشتركٌ اشترى بسعرٍ لا يتغيّر سعرُه بتحريرِ صفّ — التغييرُ إصدارٌ جديد.
        var repriced = () => plan.SetPrice(new Money(40, "JOD"), 1);
        repriced.Should().Throw<InvalidPlanException>();
        plan.Price!.Amount.Should().Be(30m);
    }

    [Fact]
    public void دورة_فوترة_خارج_المدى_مرفوضة()
    {
        var plan = Draft();
        var zero = () => plan.SetPrice(new Money(30, "JOD"), 0);
        var huge = () => plan.SetPrice(new Money(30, "JOD"), Plan.MaxBillingIntervalMonths + 1);

        zero.Should().Throw<InvalidPlanException>();
        huge.Should().Throw<InvalidPlanException>();
    }

    [Fact]
    public void سعر_صفر_ليس_كغياب_السعر()
    {
        // «سعرُها صفر» قرارٌ صريح تُصدَر له فاتورةٌ بصفر وتُغلَق؛ و«بلا سعر» تعني أنّ أحداً لم
        // يقرّر بعد — والفرقُ بينهما هو ما يمنع فاتورةً تصدر عن خطةٍ لم تُسعَّر.
        var free = Draft();
        free.SetPrice(new Money(0, "JOD"), 1);
        free.Price.Should().NotBeNull();
        free.Price!.Amount.Should().Be(0m);

        var unpriced = Draft();
        unpriced.SetPrice(null, 1);
        unpriced.Price.Should().BeNull();
    }
}
