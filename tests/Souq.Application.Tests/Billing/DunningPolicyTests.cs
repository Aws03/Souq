using AwesomeAssertions;
using Souq.Application.Features.Billing;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Billing;

// ============================================================================
// سلّمُ المطالبة (C6، [ADR-0058](0058)).
//
// **هذا أخطرُ منطقٍ في المنتج**: هو ما يُغلق متجرَ عميلٍ يدفع بلا إنسانٍ في الحلقة. ولأنّه دالّةٌ
// نقيّة، يُفحَص هنا بجدولِ حالاتٍ كاملٍ بلا خادمٍ ولا قاعدة — وهو بالضبط سببُ كونه دالّةً نقيّة.
//
// وأهمُّ ما يُثبَت: **لا تعليقَ بلا مهلةٍ مستنفَدة وإخطارٍ مستنفَد معاً.** أحدهما وحده كان
// سيُعلّق تاجراً لم يصله تذكير، أو تاجراً أُمهل يوماً واحداً.
// ============================================================================
public class DunningPolicyTests
{
    private static readonly DateTime Issued = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Due = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PlatformInvoice Overdue(int remindersSent = 0, DateTime? lastReminder = null)
    {
        var invoice = new PlatformInvoice(7, "JOD", Issued, Due);
        invoice.AddLine("اشتراك", 1, new Money(100, "JOD"));
        invoice.Issue("INV000001", Issued, Due, Money.Zero("JOD"), null,
            "سوق", null, null, "متجر", null, null);

        for (var i = 0; i < remindersSent; i++)
            invoice.RecordReminder(lastReminder ?? Due.AddDays(1));
        return invoice;
    }

    private static PlatformBillingSettings Settings(
        bool enabled = true, int grace = 7, int interval = 7, int maxReminders = 3)
    {
        var settings = PlatformBillingSettings.Empty();
        settings.SetCurrency("JOD");
        settings.SetIssuer("سوق", null, null);
        settings.SetTerms(30, grace);
        settings.SetDunning(enabled, interval, maxReminders);
        return settings;
    }

    [Fact]
    public void المطالبة_معطّلة_افتراضاً_فلا_شيء_يقع()
    {
        // نشرُ هذه الشريحة على منصّةٍ قائمة يجب ألّا يُعلّق متجراً واحداً يوم النشر.
        var fresh = PlatformBillingSettings.Empty();
        fresh.SetCurrency("JOD");
        fresh.SetIssuer("سوق", null, null);

        fresh.DunningEnabled.Should().BeFalse();
        DunningPolicy.Decide(Overdue(), fresh, Due.AddDays(400))
            .Should().Be(DunningDecision.Disabled);
    }

    [Fact]
    public void فاتورةٌ_في_مهلتها_لا_تُذكَّر()
    {
        DunningPolicy.Decide(Overdue(), Settings(), Due.AddDays(-1))
            .Should().Be(DunningDecision.NotOverdue);
    }

    [Fact]
    public void أوّلُ_تذكيرٍ_فورَ_التأخّر()
    {
        DunningPolicy.Decide(Overdue(), Settings(), Due.AddDays(1))
            .Should().Be(DunningDecision.Remind);
    }

    [Fact]
    public void التذكيرُ_التالي_يُقاس_من_آخر_تذكيرٍ_لا_من_الاستحقاق()
    {
        // منصّةٌ تعطّلت دورتُها يومين ثمّ عادت يجب ألّا تُرسل التذكيرات الفائتة دفعةً واحدة.
        var lastReminder = Due.AddDays(1);
        var invoice = Overdue(remindersSent: 1, lastReminder: lastReminder);

        DunningPolicy.Decide(invoice, Settings(interval: 7), lastReminder.AddDays(6))
            .Should().Be(DunningDecision.ReminderNotDue);
        DunningPolicy.Decide(invoice, Settings(interval: 7), lastReminder.AddDays(7))
            .Should().Be(DunningDecision.Remind);
    }

    // ── التعليق: الشرطان معاً ────────────────────────────────────────────────

    [Fact]
    public void لا_تعليقَ_بمهلةٍ_مستنفَدة_وحدها()
    {
        // مرّت المهلة بكثير، ولم يصل التاجرَ تذكيرٌ واحد: يُذكَّر، لا يُعلَّق.
        DunningPolicy.Decide(Overdue(remindersSent: 0), Settings(grace: 7, maxReminders: 3), Due.AddDays(60))
            .Should().Be(DunningDecision.Remind);
    }

    [Fact]
    public void لا_تعليقَ_بتذكيراتٍ_مستنفَدة_وحدها()
    {
        // ثلاثةُ تذكيراتٍ متسارعة داخل مهلة السماح لا تُعلّق: المهلةُ لم تمرّ بعد.
        var invoice = Overdue(remindersSent: 3, lastReminder: Due.AddDays(1));

        DunningPolicy.Decide(invoice, Settings(grace: 30, maxReminders: 3), Due.AddDays(5))
            .Should().Be(DunningDecision.WithinGrace);
    }

    [Fact]
    public void التعليق_يقع_حين_يُمهَل_التاجر_ويُخطَر_معاً()
    {
        var invoice = Overdue(remindersSent: 3, lastReminder: Due.AddDays(1));

        DunningPolicy.Decide(invoice, Settings(grace: 7, maxReminders: 3), Due.AddDays(8))
            .Should().Be(DunningDecision.Suspend);
    }

    [Fact]
    public void حدُّ_المهلة_نفسُه_ليس_تجاوزاً()
    {
        // «تجاوزَ المهلة» تعني أكثر منها لا مساويها: يومُ انتهاء المهلة نفسُه ما زال مهلةً.
        var invoice = Overdue(remindersSent: 3, lastReminder: Due.AddDays(1));

        DunningPolicy.Decide(invoice, Settings(grace: 7, maxReminders: 3), Due.AddDays(7))
            .Should().Be(DunningDecision.WithinGrace);
    }

    [Fact]
    public void لا_تُصعَّد_فاتورةٌ_صُعِّدت_ولا_تُذكَّر()
    {
        var invoice = Overdue(remindersSent: 3, lastReminder: Due.AddDays(1));
        invoice.RecordEscalation(Due.AddDays(8));

        DunningPolicy.Decide(invoice, Settings(grace: 7, maxReminders: 3), Due.AddDays(40))
            .Should().Be(DunningDecision.AlreadyEscalated);
    }

    [Fact]
    public void فاتورةٌ_سُدّدت_تخرج_من_السلّم_فوراً()
    {
        var invoice = Overdue(remindersSent: 3, lastReminder: Due.AddDays(1));
        invoice.RecordPayment(new Money(100, "JOD"), PlatformPaymentMethod.BankTransfer, Due.AddDays(8), 3, null, null);

        invoice.Status.Should().Be(PlatformInvoiceStatus.Settled);
        DunningPolicy.Decide(invoice, Settings(grace: 7, maxReminders: 3), Due.AddDays(40))
            .Should().Be(DunningDecision.NotOverdue);
    }

    [Fact]
    public void صفرُ_تذكيراتٍ_يعني_تعليقاً_بمجرّد_انقضاء_المهلة()
    {
        // مشغّلٌ يختار ألّا يُذكّر أصلاً خيارٌ مشروع — والسلّم يحترمه بلا حالةٍ خاصّة.
        DunningPolicy.Decide(Overdue(remindersSent: 0), Settings(grace: 7, maxReminders: 0), Due.AddDays(8))
            .Should().Be(DunningDecision.Suspend);
    }

    [Fact]
    public void لا_تُفعَّل_المطالبة_بمهلة_سماحٍ_صفر()
    {
        // تعليقٌ في اليوم التالي للاستحقاق لا يقصده أحد، ولو قصده لكتبه يوماً واحداً.
        var settings = PlatformBillingSettings.Empty();
        settings.SetTerms(30, 0);

        var enable = () => settings.SetDunning(true, 7, 3);
        enable.Should().Throw<Souq.Domain.Exceptions.InvalidPlatformBillingSettingsException>();
    }
}
