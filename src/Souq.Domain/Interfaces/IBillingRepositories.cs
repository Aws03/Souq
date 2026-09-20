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
