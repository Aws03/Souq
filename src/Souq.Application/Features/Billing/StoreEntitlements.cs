using Souq.Application.Features.Billing.Contracts;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Billing;

// تنفيذ IStoreEntitlements: الطرف الوحيد الذي يعرف أن "الخطة التأسيسية" موجودة أصلاً، وكيف يُشترَك
// عليها. المتجر الجديد يُنشئه Platform ثم يطلب منه عقداً — ولا يعرف عنه إلا أنه تمّ أو لم يتمّ.
internal sealed class StoreEntitlements : IStoreEntitlements
{
    private readonly IPlanRepository _plans;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public StoreEntitlements(
        IPlanRepository plans, ISubscriptionRepository subscriptions, TimeProvider clock, IUnitOfWork uow)
    {
        _plans = plans; _subscriptions = subscriptions; _clock = clock; _uow = uow;
    }

    public async Task<StorePlanSummary?> GetPlanSummaryAsync(int tenantId, CancellationToken ct = default)
    {
        // الاشتراك الملغى لا يمنح شيئاً، فهو كغيابه تماماً في هذا العرض.
        if (await _subscriptions.FindByTenantAsync(tenantId, ct) is not { Status: SubscriptionStatus.Active } sub)
            return null;
        if (await _plans.GetByIdAsync(sub.PlanId, ct) is not { } plan) return null;
        return new StorePlanSummary(plan.Id, plan.Code, plan.Name, plan.Version, plan.Status.ToString());
    }

    public async Task<bool> AssignFoundationPlanAsync(int tenantId, CancellationToken ct = default)
    {
        if (await _plans.FindLatestPublishedAsync(Plan.FoundationCode, ct) is not { } foundation) return false;

        // محتمل للتكرار: متجرٌ له اشتراك أصلاً لا يُصطدم بالفهرس الفريد على TenantId.
        if (await _subscriptions.FindByTenantAsync(tenantId, ct) is not null) return true;

        await _subscriptions.AddAsync(new Subscription(tenantId, foundation, _clock.GetUtcNow().UtcDateTime), ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
