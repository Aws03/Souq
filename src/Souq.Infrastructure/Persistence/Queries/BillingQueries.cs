using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Billing;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// BillingQueries — قراءات مستوى التحكّم التجاري (C1، ADR-0047). الانضباط هو انضباط PlatformQueries
// نفسه، ولسببٍ أقوى: هذه الجداول **لا مرشّح مستأجر عليها أصلاً**، فليس هنا تجاوزٌ يُرى في الكود،
// بل غيابُ شبكة أمان بالكامل. القاعدة إذن:
//   • كل قراءة تخصّ متجراً بعينه تحمل شرط `TenantId ==` صريحاً — بلا استثناء،
//   • وما يعبر المتاجر عدٌّ مجمَّع لا يعيد صفّ متجرٍ بعينه (عدد المشتركين على خطة)،
//   • ولا تُستدعى إلا من حالات استخدام منطقة المنصّة، مُدقَّقة وخلف صلاحية منصّة ومضيفها.
// وهذا الصنف مدرَج في `ReviewedPlatformKeyedReads` بـ TenancyRuleTests: إضافة صنفٍ آخر يقرأ هذه
// الجداول قرارٌ يُراجَع، لا سطرٌ يمرّ.
//
// الخطط (Plans وأبناؤها) عالمية بلا متجر، فتُقرأ كأي جدول.
// ============================================================================
internal sealed class BillingQueries : IBillingQueries
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public BillingQueries(AppDbContext db, TimeProvider clock)
    {
        _db = db; _clock = clock;
    }

    public Task<PaginatedList<PlanSummaryDto>> ListPlansAsync(PlanListFilter filter, PageRequest page, CancellationToken ct)
    {
        var plans = _db.Plans.AsNoTracking();
        if (!filter.IncludeRetired)
            plans = plans.Where(p => p.Status != PlanStatus.Retired);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            plans = plans.Where(p => p.Code.Contains(search) || p.Name.Contains(search));
        }

        return plans
            .OrderBy(p => p.Code).ThenByDescending(p => p.Version).ThenByDescending(p => p.Id)
            .ToPageAsync(p => new PlanSummaryDto(
                p.Id, p.Code, p.Version, p.Name, p.Status.ToString(),
                // عدٌّ مجمَّع عبر المتاجر: كم متجراً يسري عليه هذا الإصدار — لا صفّ متجرٍ بعينه.
                _db.Subscriptions.Count(s => s.PlanId == p.Id && s.Status == SubscriptionStatus.Active)),
                page, ct);
    }

    public async Task<PlanDetailDto?> GetPlanAsync(int planId, CancellationToken ct) =>
        await _db.Plans.AsNoTracking()
            .Where(p => p.Id == planId)
            .Select(p => new PlanDetailDto(
                p.Id, p.Code, p.Version, p.Name, p.Status.ToString(),
                p.Entitlements.Select(e => e.Entitlement).OrderBy(e => e).ToList(),
                p.Limits.Select(l => new PlanLimitDto(l.Name, l.Value)).OrderBy(l => l.Name).ToList(),
                _db.Subscriptions.Count(s => s.PlanId == p.Id && s.Status == SubscriptionStatus.Active),
                p.CreatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<TenantEntitlementsDto?> GetTenantEntitlementsAsync(int tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Id, Enabled = EF.Property<string>(t, "_modules") })
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return null;

        var now = _clock.GetUtcNow().UtcDateTime;

        // شرط TenantId صريح — الجدول بلا مرشّح.
        var subscription = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new { s.PlanId, s.Status, s.StartedAtUtc })
            .FirstOrDefaultAsync(ct);

        var plan = subscription is null ? null : await _db.Plans.AsNoTracking()
            .Where(p => p.Id == subscription.PlanId)
            .Select(p => new
            {
                p.Id, p.Code, p.Version, p.Name,
                Entitlements = p.Entitlements.Select(e => e.Entitlement).ToList(),
                Limits = p.Limits.Select(l => new PlanLimitDto(l.Name, l.Value)).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        // الاشتراك الملغى لا يمنح — الشرط نفسه الذي يطبّقه TenantDirectory، كي لا تختلف الشاشة عن الفرض.
        var active = subscription?.Status == SubscriptionStatus.Active;
        var planGrants = active && plan is not null ? plan.Entitlements : [];

        var overrides = await _db.EntitlementOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.RevokedAtUtc == null && o.ExpiresAtUtc > now)
            .Select(o => o.Entitlement)
            .ToListAsync(ct);

        var enabled = StoreModules.Parse(tenant.Enabled);
        var granted = Entitlements.Granted(planGrants, overrides);

        return new TenantEntitlementsDto(
            tenantId, plan?.Id, plan?.Code, plan?.Version, plan?.Name,
            subscription?.Status.ToString(), subscription?.StartedAtUtc,
            planGrants.Order(StringComparer.Ordinal).ToList(),
            overrides.Order(StringComparer.Ordinal).ToList(),
            enabled.Order(StringComparer.Ordinal).ToList(),
            Entitlements.Effective(granted, enabled).Order(StringComparer.Ordinal).ToList(),
            plan is null ? [] : plan.Limits.OrderBy(l => l.Name, StringComparer.Ordinal).ToList());
    }

    public async Task<IReadOnlyList<EntitlementOverrideDto>> ListOverridesAsync(int tenantId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // الانضمام لـ Users بلا تجاوز: الحساب المانح حساب منصّة، ومرشّح النطاق يعيد صفوف المنصّة هنا.
        return await _db.EntitlementOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .OrderByDescending(o => o.ExpiresAtUtc).ThenByDescending(o => o.Id)
            .Select(o => new EntitlementOverrideDto(
                o.Id, o.Entitlement, o.ExpiresAtUtc, o.Reason, o.GrantedByUserId,
                _db.Users.Where(u => u.Id == o.GrantedByUserId).Select(u => u.Email).FirstOrDefault(),
                o.RevokedAtUtc,
                o.RevokedAtUtc == null && o.ExpiresAtUtc > now))
            .ToListAsync(ct);
    }
}
