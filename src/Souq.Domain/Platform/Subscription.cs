using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// حالة الاشتراك بمفردات **سوق نفسها** لا بمفردات مزوّد (ADR-0047، CommercialPlatformArchitecture §4.6):
// مفردات المزوّدين تتناقض — في أحدها `canceled` نهائية وفي آخر تعني "تنتهي بنهاية المدّة".
// C1 يحمل الحالتين اللتين لا تحتاجان فوترة؛ ما تضيفه الفوترة (متأخّر، تجربة) يأتي مع C5/C6 وقرار المالك.
public enum SubscriptionStatus
{
    Active = 0,
    Cancelled = 1,
}

// ============================================================================
// Subscription — أي إصدار خطة يسري على متجر. جدول منصّة **بمفتاح متجر** (الشكل B في ADR-0047 §1):
// يعيش في Souq.Domain.Platform، يحمل TenantId عموداً عادياً، و**لا يطبَّق عليه مرشّح المستأجر**.
// فعزله يقوم كلّه على شرط TenantId صريح يكتبه المستدعي في كل قراءة — وهو ما يحرسه اليوم
// `قراءة_جداول_المنصّة_بمفتاح_متجر_محصورة_في_مسارها_المراجَع` في TenancyRuleTests.
//
// صفّ واحد لكل متجر (فهرس فريد على TenantId): "ما خطة هذا المتجر؟" سؤال له جواب واحد. تاريخ
// التغييرات يحمله سجلّ التدقيق لا صفوف متراكمة — والفوترة (C5) هي ما سيحتاج مُدداً وفتراتٍ.
// ============================================================================
public class Subscription : Entity
{
    public int TenantId { get; private set; }
    public int PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }

    private Subscription() { }

    public Subscription(int tenantId, Plan plan, DateTime utcNow)
    {
        if (tenantId <= 0) throw new InvalidSubscriptionException("اشتراك بلا متجر");
        TenantId = tenantId;
        Start(plan, utcNow);
    }

    // لا يُشترَك إلا على خطة **منشورة**: المسوّدة لم تُقرّ شروطها، والمتقاعدة لم تعد تُباع.
    // المشترك على خطة تقاعدت لاحقاً يبقى عليها — ولهذا الفحص هنا عند الإسناد لا عند القراءة.
    public void ChangePlan(Plan plan, DateTime utcNow) => Start(plan, utcNow);

    public void Cancel(DateTime utcNow)
    {
        if (Status == SubscriptionStatus.Cancelled) return;
        Status = SubscriptionStatus.Cancelled;
        EndedAtUtc = utcNow;
    }

    private void Start(Plan plan, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status != PlanStatus.Published)
            throw new InvalidSubscriptionException($"لا يُشترَك إلا على خطة منشورة (حالة {plan.Code}/{plan.Version}: {plan.Status})");
        PlanId = plan.Id;
        Status = SubscriptionStatus.Active;
        StartedAtUtc = utcNow;
        EndedAtUtc = null;
    }
}
