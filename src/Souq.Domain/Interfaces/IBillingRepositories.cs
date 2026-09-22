using Souq.Domain.Platform;

namespace Souq.Domain.Interfaces;

// ============================================================================
// منافذ الكتابة لوحدة Billing (C1، ADR-0047). جداول منصّة: الخطط عالمية بلا متجر (الشكل C)،
// والاشتراكات والاستثناءات بمفتاح متجر بلا مرشّح (الشكل B) — فكل قراءة هنا تحمل TenantId صريحاً،
// ولا واحدة منها تُستدعى إلا من حالة استخدام في منطقة المنصّة خلف صلاحية منصّة.
// القراءات للعرض تمرّ بـ IBillingQueries لا بهذه (ADR-0008).
// ============================================================================

public interface IPlanRepository : IRepository<Plan>
{
    // الخطة باستحقاقاتها وحدودها: الاشتراك يحتاج شروطها لا صفّها وحده.
    Task<Plan?> GetWithTermsAsync(int id, CancellationToken ct = default);

    Task<Plan?> FindAsync(string code, int version, CancellationToken ct = default);

    // الإصدار التالي لمعرّف خطة (1 إن لم توجد): التعديل بعد النشر إصدارٌ جديد لا تحرير.
    Task<int> NextVersionAsync(string code, CancellationToken ct = default);

    // أحدث إصدار **منشور** لمعرّف خطة — ما يُشترَك عليه اليوم. يُستعمل لإسناد الخطة التأسيسية
    // لمتجر جديد؛ null إن لم تُنشر بعد، وعندها يُنشأ المتجر بلا اشتراك (يفشل مغلقاً).
    Task<Plan?> FindLatestPublishedAsync(string code, CancellationToken ct = default);
}

public interface ISubscriptionRepository : IRepository<Subscription>
{
    // صفّ واحد لكل متجر (فهرس فريد): "ما خطة هذا المتجر؟" له جواب واحد.
    Task<Subscription?> FindByTenantAsync(int tenantId, CancellationToken ct = default);
}

public interface IEntitlementOverrideRepository : IRepository<EntitlementOverride>
{
    // استثناء سارٍ لهذا المتجر ولهذا الاستحقاق بعينه — كي لا يتكدّس استثناءان على استحقاق واحد.
    Task<EntitlementOverride?> FindActiveAsync(int tenantId, string entitlement, DateTime utcNow, CancellationToken ct = default);
}

// ── فوترةُ التاجر (C5، ADR-0056) ─────────────────────────────────────────────
//
// كلُّ قراءةٍ هنا تخصّ متجراً تحمل `tenantId` **وسيطاً صريحاً**: هذه جداولُ الشكل B، بلا مرشّحٍ
// وبلا حارسِ كتابة، فعزلُها هو الشرطُ الذي يكتبه المستدعي بيده — وهو ما يحرسه
// `قراءة_جداول_المنصّة_بمفتاح_متجر_محصورة_في_مسارها_المراجَع` في `TenancyRuleTests`.

// إعدادُ فوترة المنصّة: صفٌّ واحد عالميّ. `null` ⇒ لم يُنشَأ بعد ⇒ لا إصدار.
public interface IPlatformBillingSettingsRepository
{
    Task<PlatformBillingSettings?> GetAsync(CancellationToken ct = default);

    void Add(PlatformBillingSettings settings);
}

public interface IPlatformInvoiceRepository : IRepository<PlatformInvoice>
{
    // الفاتورةُ بأسطرها ومسدَّداتها: كلُّ قرارٍ عليها يحتاج الشجرة لا الجذر — الإصدارُ يفحص
    // الأسطر، وتسجيلُ السداد يحتاج المتبقّي، والمتبقّي محتسَبٌ من الاثنين.
    Task<PlatformInvoice?> GetWithDetailsAsync(int id, CancellationToken ct = default);

    // فاتورةٌ بعينها لمتجرٍ بعينه. الوسيطان معاً لا المعرّف وحده: هذا ما يجعل تاجراً لا يقرأ
    // فاتورةَ تاجرٍ آخر بتخمين رقم — و404 هو الجوابُ الصحيح لا 403 (عُرفُ المستودع).
    Task<PlatformInvoice?> GetForTenantAsync(int id, int tenantId, CancellationToken ct = default);

    // ============================================================================
    // الفواتيرُ الصادرة التي مرّ استحقاقُها (C6، ADR-0058) — **قراءةٌ تعبر المتاجر عمداً**، لأنّ
    // المطالبة عملُ منصّةٍ لا عملُ متجر: سلّمٌ يعمل لكل متجر على حدة كان سيحتاج دورةً لكل متجر
    // وقفلاً لكل متجر، بلا أن يشتري شيئاً.
    //
    // ولا تُستدعى إلّا من منسّق المطالبة، وهو وحيد ومحروسٌ بعقد إيجار. والحدُّ الأعلى يمنع دورةً
    // تُحمّل عشرات الآلاف من الصفوف بعد إهمالٍ طويل.
    // ============================================================================
    Task<IReadOnlyList<PlatformInvoice>> ListOverdueAsync(DateTime utcNow, int max, CancellationToken ct = default);
}

public interface ICreditNoteRepository : IRepository<CreditNote>
{
    Task<CreditNote?> GetWithLinesAsync(int id, CancellationToken ct = default);

    // إشعاراتُ فاتورةٍ بعينها — تُقرأ مع الفاتورة كي يُعرف ما قُيّد دائناً وبأيّ سبب.
    Task<IReadOnlyList<CreditNote>> ListForInvoiceAsync(int platformInvoiceId, CancellationToken ct = default);
}

public interface IBillingPeriodRepository : IRepository<BillingPeriod>
{
    // ============================================================================
    // الفترةُ التي تسع هذه اللحظة لهذا المتجر — **بأيّ حالة**، إن وُجدت.
    //
    // وحالتُها مقصودةٌ في النتيجة لا مُرشَّحةٌ منها: «لا فترةَ مفتوحة» و«الفترةُ أُغلقت» جوابان
    // مختلفان تماماً — الأوّلُ يُفتح له فترة، والثاني يُرفض الحدثُ عنده لأنّ فاتورتَها قد صدرت.
    // وترشيحُ المغلقة هنا كان سيجعل الثانيَ يُقرأ كالأول، فيلتحق حدثٌ متأخّرٌ بفترةٍ جديدة
    // تحمل تاريخَ فترةٍ مغلقة.
    // ============================================================================
    Task<BillingPeriod?> FindCoveringAsync(int tenantId, DateTime instant, CancellationToken ct = default);

    Task<BillingPeriod?> GetForTenantAsync(int id, int tenantId, CancellationToken ct = default);
}

public interface IBillableEventRepository : IRepository<BillableEvent>
{
    // بحثٌ بمفتاح عدم التكرار: إعادةُ محاولةٍ بعد انقطاعٍ شبكيّ لا تُفوتر الوحدةَ مرّتين. الفهرسُ
    // الفريد هو الحاسم في القاعدة، وهذا هو الفحصُ اللطيف قبله.
    Task<BillableEvent?> FindByKeyAsync(int tenantId, string idempotencyKey, CancellationToken ct = default);

    // أحداثُ فترةٍ لم تُحمَّل على فاتورةٍ بعد — ما تقرؤه صناعةُ الفاتورة كي لا تُفوتر وحدةً مرّتين.
    Task<IReadOnlyList<BillableEvent>> ListUnbilledAsync(int tenantId, int billingPeriodId, CancellationToken ct = default);
}
