using Souq.Domain.Platform;

namespace Souq.Application.Features.Billing;

// ============================================================================
// ما يجب أن يقع على فاتورةٍ متأخّرة، الآن (C6، [ADR-0058](0058)).
//
// **دالّةٌ نقيّة، ولا شيء فيها يقرأ قاعدةً ولا ساعةً ولا يُرسل شيئاً.** كلُّ ما يلزمها يصل وسائطَ،
// وجوابُها قرارٌ لا فعل — ومَن ينفّذه هو المنسّق. وهذا هو ما يجعل **أخطر منطقٍ في المنتج** قابلاً
// للفحص بجدولِ حالاتٍ بلا خادمٍ ولا قاعدة.
//
// **وخطورتُه ليست مبالغة**: هذه أوّلُ قدرةٍ تُغلق متجرَ عميلٍ يدفع، بلا إنسانٍ في الحلقة. فالسلّم
// كلُّه مكتوبٌ في مكانٍ واحد، ويُقرأ في دقيقة:
//
//   1. **المطالبةُ معطّلة** ⇒ لا شيء. (الافتراضُ، ويبقى حتى يُفعّلها إنسان.)
//   2. **الفاتورةُ ليست متأخّرة** ⇒ لا شيء.
//   3. **مرّت مهلةُ السماح، واستُنفدت التذكيرات** ⇒ تعليق.
//   4. **حان وقتُ تذكيرٍ** ⇒ تذكير.
//   5. غير ذلك ⇒ لا شيء (ننتظر).
//
// والترتيبُ نفسُه قرار: التعليقُ يسبق التذكير في الفحص، فمتجرٌ تجاوز كلَّ المهل لا يُذكَّر مرّةً
// أخرى قبل أن يُعلَّق — والتذكيرُ بعد استنفاد السلّم تأجيلٌ بلا نهاية.
// ============================================================================
public enum DunningAction
{
    None = 0,
    Remind = 1,
    Suspend = 2,
}

// القرارُ وسببُه. **السببُ ليس زينة**: كلُّ دورةٍ تُسجَّل، ومشغّلٌ يقرأ «لم يُفعل شيء» بلا سبب لا
// يعرف أهي مطالبةٌ معطّلة أم فاتورةٌ في مهلتها.
public sealed record DunningDecision(DunningAction Action, string Reason)
{
    public static readonly DunningDecision Disabled = new(DunningAction.None, "DunningDisabled");
    public static readonly DunningDecision NotOverdue = new(DunningAction.None, "NotOverdue");
    public static readonly DunningDecision WithinGrace = new(DunningAction.None, "WithinGracePeriod");
    public static readonly DunningDecision ReminderNotDue = new(DunningAction.None, "ReminderNotDue");
    public static readonly DunningDecision AlreadyEscalated = new(DunningAction.None, "AlreadyEscalated");
    public static readonly DunningDecision Remind = new(DunningAction.Remind, "ReminderDue");
    public static readonly DunningDecision Suspend = new(DunningAction.Suspend, "GraceExhausted");
}

public static class DunningPolicy
{
    // ========================================================================
    // القرار. `utcNow` وسيطٌ لا `DateTime.UtcNow`: قاعدةٌ تُقاس بالزمن لا تُختبر إن قرأت الساعة
    // بنفسها، وهي القاعدة نفسها التي يفرضها `ClockRuleTests` على المنتج كلّه.
    // ========================================================================
    public static DunningDecision Decide(PlatformInvoice invoice, PlatformBillingSettings settings, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.DunningEnabled) return DunningDecision.Disabled;

        // «متأخّرة» تُسأل للفاتورة نفسها لا تُعاد هنا: `Issued` تعني أنّ عليها شيئاً (الحالةُ
        // تصير `Settled` عند بلوغ الصفر)، والتأخّرُ دالّةُ لحظةٍ لا حقلٌ مخزَّن.
        if (!invoice.IsOverdueAt(utcNow)) return DunningDecision.NotOverdue;

        // فاتورةٌ صعّدت من قبل لا تُصعّد ثانيةً ولا تُذكّر: المتجرُ معلَّقٌ أصلاً، والتذكيرُ بعد
        // التعليق ضجيج.
        if (invoice.EscalatedAtUtc is not null) return DunningDecision.AlreadyEscalated;

        var daysOverdue = invoice.DaysOverdueAt(utcNow);
        var graceExhausted = daysOverdue > settings.GracePeriodDays;
        var remindersExhausted = invoice.RemindersSent >= settings.MaxRemindersBeforeSuspension;

        // **الشرطان معاً، لا أحدهما.** مهلةُ السماح وحدها كانت ستُعلّق متجراً لم يصله تذكيرٌ
        // واحد؛ والتذكيراتُ وحدها كانت ستُعلّقه بعد ثلاثة تذكيراتٍ متسارعة مهما كانت المهلة.
        // فالتعليقُ يقع حين يكون التاجر قد **أُمهل** و**أُخطر**.
        if (graceExhausted && remindersExhausted) return DunningDecision.Suspend;

        if (!graceExhausted && remindersExhausted) return DunningDecision.WithinGrace;

        return ReminderDue(invoice, settings, utcNow) ? DunningDecision.Remind : DunningDecision.ReminderNotDue;
    }

    // ========================================================================
    // أوّلُ تذكيرٍ يقع فور التأخّر؛ وما بعده يفصله `ReminderIntervalDays` عن سابقه.
    //
    // والفاصلُ يُقاس من **آخر تذكيرٍ أُرسل** لا من تاريخ الاستحقاق: منصّةٌ تعطّلت دورتُها يومين
    // ثمّ عادت يجب ألّا تُرسل التذكيرات الفائتة دفعةً واحدة — وهو ما كان يقع لو قِيس من الاستحقاق.
    // ========================================================================
    private static bool ReminderDue(PlatformInvoice invoice, PlatformBillingSettings settings, DateTime utcNow) =>
        invoice.LastReminderAtUtc is not { } last
        || utcNow >= last.AddDays(settings.ReminderIntervalDays);
}
