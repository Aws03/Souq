using Microsoft.EntityFrameworkCore;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Repositories;

// ============================================================================
// منافذ كتابة وحدة Billing (C1، ADR-0047). جداول منصّة: لا مرشّح مستأجر يحميها.
//   • Plans وأبناؤها عالمية (الشكل C) — تُقرأ كأي جدول.
//   • Subscriptions وEntitlementOverrides بمفتاح متجر (الشكل B) — **كل قراءة هنا تحمل شرط
//     TenantId صريحاً**، وهي الانضباط الذي يقوم عليه عزلها بالكامل. هذا الملف مدرَج عمداً في
//     ReviewedPlatformKeyedReads بـ TenancyRuleTests: لا أحد يقرأ هذين الجدولين بلا مراجعة.
// ============================================================================

public class PlanRepository : RepositoryBase<Plan>, IPlanRepository
{
    public PlanRepository(AppDbContext db) : base(db) { }

    public Task<Plan?> GetWithTermsAsync(int id, CancellationToken ct = default) =>
        Db.Plans.Include(p => p.Entitlements).Include(p => p.Limits).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Plan?> FindAsync(string code, int version, CancellationToken ct = default)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? "";
        return Db.Plans.Include(p => p.Entitlements).Include(p => p.Limits)
            .FirstOrDefaultAsync(p => p.Code == normalized && p.Version == version, ct);
    }

    public Task<Plan?> FindLatestPublishedAsync(string code, CancellationToken ct = default)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? "";
        return Db.Plans.Where(p => p.Code == normalized && p.Status == PlanStatus.Published)
            .OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
    }

    public async Task<int> NextVersionAsync(string code, CancellationToken ct = default)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? "";
        var highest = await Db.Plans.Where(p => p.Code == normalized)
            .Select(p => (int?)p.Version).MaxAsync(ct);
        return (highest ?? 0) + 1;
    }
}

public class SubscriptionRepository : RepositoryBase<Subscription>, ISubscriptionRepository
{
    public SubscriptionRepository(AppDbContext db) : base(db) { }

    // شرط TenantId صريح: الجدول بلا مرشّح، فلا "أعطني الاشتراك" بلا متجر.
    public Task<Subscription?> FindByTenantAsync(int tenantId, CancellationToken ct = default) =>
        Db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
}

public class EntitlementOverrideRepository : RepositoryBase<EntitlementOverride>, IEntitlementOverrideRepository
{
    public EntitlementOverrideRepository(AppDbContext db) : base(db) { }

    public Task<EntitlementOverride?> FindActiveAsync(int tenantId, string entitlement, DateTime utcNow, CancellationToken ct = default)
    {
        var key = entitlement?.Trim().ToLowerInvariant() ?? "";
        return Db.EntitlementOverrides.FirstOrDefaultAsync(
            o => o.TenantId == tenantId && o.Entitlement == key && o.RevokedAtUtc == null && o.ExpiresAtUtc > utcNow, ct);
    }
}
