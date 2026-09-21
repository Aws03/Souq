using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Models;
using Souq.Application.Features.Platform;
using Souq.Application.Features.Reporting;
using Souq.Application.Features.Stores;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// PlatformQueries — الصنف الوحيد المسموح له بتجاوز مرشّح المستأجر (TenancyRuleTests). كل تجاوز هنا:
//   • بشرط TenantId صريح حين يخصّ متجراً بعينه (حسابات إدارته، نشاطه التجاري)،
//   • أو عدٌّ مجمَّع عبر المتاجر لا يعيد صفاً واحداً (إحصاءات المنصّة)،
//   • ويُستدعى من حالات استخدام منطقة المنصّة وحدها — مُدقَّقة (IAuditable) وخلف صلاحياتها ومضيفها.
// جداول Tenants وTenantDomains وAuditEntries بلا مرشّح أصلاً (جداول المنصّة).
// ============================================================================
internal sealed class PlatformQueries : IPlatformQueries, IPlatformReports
{
    private static readonly string[] TenantFilter = [AppDbContext.TenantFilter];

    private readonly AppDbContext _db;
    public PlatformQueries(AppDbContext db) => _db = db;

    public async Task<PaginatedList<TenantSummaryDto>> ListTenantsAsync(TenantListFilter filter, PageRequest page, CancellationToken ct)
    {
        var tenants = _db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search;
            var lowered = term.ToLowerInvariant();
            tenants = tenants.Where(t => t.Name.Contains(term) || t.Slug.Contains(lowered)
                                         || t.Domains.Any(d => d.Host.Contains(lowered)));
        }
        if (filter.Status is { } status)
            tenants = tenants.Where(t => t.Status == status);

        var rows = await tenants.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .ToPageAsync(t => new TenantRow(
                t.Id, t.Name, t.Slug, t.Status, t.Currency, t.DefaultCulture,
                t.Domains.Where(d => d.IsPrimary).Select(d => d.Host).FirstOrDefault(), t.Domains.Count(), t.CreatedAt), page, ct);

        // حالة مديري متاجر هذه الصفحة وحدها، في استعلام ثانٍ مجمَّع: عبور مرشّح المستأجر هنا بشرط TenantId صريح
        // على معرّفات الصفحة — لا عدّ لحسابات متاجر لا تُعرض.
        var ids = rows.Items.Select(r => r.Id).ToList();
        var admins = await _db.Users.IgnoreQueryFilters(TenantFilter).AsNoTracking()
            .Where(u => u.TenantId != null && ids.Contains(u.TenantId.Value) && u.Role == Roles.TenantAdmin
                        && u.Status == UserStatus.Active)
            .GroupBy(u => u.TenantId!.Value)
            .Select(g => new { TenantId = g.Key, Active = g.Count(u => u.PasswordHash != ""), Pending = g.Count(u => u.PasswordHash == "") })
            .ToDictionaryAsync(x => x.TenantId, ct);

        return rows.Map(r => new TenantSummaryDto(
            r.Id, r.Name, r.Slug, r.Status.ToString(), r.Currency, r.DefaultCulture, r.PrimaryHost, r.DomainCount, r.CreatedAt,
            admins.TryGetValue(r.Id, out var a) ? a.Active : 0, admins.TryGetValue(r.Id, out var p) ? p.Pending : 0));
    }

    public async Task<TenantDetailDto?> GetTenantAsync(int tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().Include(t => t.Domains).FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return null;

        return new TenantDetailDto(
            tenant.Id, tenant.Name, tenant.Slug, tenant.Status.ToString(), tenant.Currency, tenant.DefaultCulture,
            tenant.TimeZone, tenant.CreatedAt,
            tenant.Domains.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Host, StringComparer.Ordinal)
                .Select(d => new TenantDomainDto(d.Host, d.IsPrimary, d.VerifiedAt)).ToList(),
            tenant.Modules.Order(StringComparer.Ordinal).ToList(),
            StoreSettingsMapper.ToDto(tenant),
            // الجواب الواحد وخطّته يملؤهما GetTenantHandler — قاعدة الدمج في مكان واحد (C1).
            [], null, null);
    }

    // حسابات إدارة المتجر (لا عملاؤه) — تجاوز بشرط المتجر الصريح.
    public async Task<PaginatedList<AccountSummaryDto>> ListTenantAccountsAsync(int tenantId, PageRequest page, CancellationToken ct) =>
        (await _db.Users.IgnoreQueryFilters(TenantFilter).AsNoTracking()
            .Where(u => u.TenantId == tenantId && (u.Role == Roles.TenantAdmin || u.Role == Roles.TenantStaff))
            .OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .ToPageAsync(AccountQueries.Row, page, ct))
        .Map(AccountQueries.ToDto);

    // "هل للمتجر نشاط تجاري؟" — يقفل تغيير العملة (Tenant.ChangeCurrency).
    //
    // الكوبونات وطرق الشحن صفوف مُسعَّرة أيضاً (R-09): كانت خارج الحساب، فيغيّر متجر عملته بعد إنشائها فتبقى بعملتها
    // القديمة. أثر ذلك مختلف بين الاثنين — طريقة الشحن تُخفى (BR-SHP-05) وRateFor ترمي، أما الكوبون فمبلغه الثابت
    // decimal بلا عملة إطلاقاً، فيصير "خصم 5 دنانير" خصمَ 5 دولارات بصمت. المنع عند المصدر أرخص من كشفه لاحقاً،
    // وعلّة القاعدة نفسها (BR-TEN-18) تشملهما: سعر كُتب بالعملة القديمة يتغيّر معناه.
    public async Task<bool> HasCommercialActivityAsync(int tenantId, CancellationToken ct) =>
        await _db.Products.IgnoreQueryFilters(TenantFilter).AnyAsync(p => p.TenantId == tenantId, ct)
        || await _db.Orders.IgnoreQueryFilters(TenantFilter).AnyAsync(o => o.TenantId == tenantId, ct)
        || await _db.Coupons.IgnoreQueryFilters(TenantFilter).AnyAsync(c => c.TenantId == tenantId, ct)
        || await _db.ShippingMethods.IgnoreQueryFilters(TenantFilter).AnyAsync(s => s.TenantId == tenantId, ct);

    public async Task<PaginatedList<AuditEntryDto>> ListAuditAsync(AuditFilter filter, PageRequest page, CancellationToken ct)
    {
        var entries = _db.AuditEntries.AsNoTracking();
        if (filter.TenantId is { } tenantId) entries = entries.Where(a => a.TenantId == tenantId);
        if (filter.Action is { Length: > 0 } action) entries = entries.Where(a => a.Action.StartsWith(action));
        if (filter.ActorUserId is { } actor) entries = entries.Where(a => a.ActorUserId == actor);
        if (filter.From is { } from) entries = entries.Where(a => a.OccurredAt >= from);
        if (filter.To is { } to) entries = entries.Where(a => a.OccurredAt <= to);

        return await entries.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .ToPageAsync(a => new AuditEntryDto(
                a.Id, a.OccurredAt, a.Area, a.Action, a.TenantId, a.ActorUserId, a.ActorRole,
                a.TargetType, a.TargetId, a.Metadata, a.IpAddress, a.CorrelationId), page, ct);
    }

    // أعداد مجمَّعة عبر المتاجر — لا صف متجر يعبر إلى المنصّة.
    public async Task<PlatformStatsDto> GetStatsAsync(DateTime recentSince, CancellationToken ct)
    {
        var byStatus = await _db.Tenants.AsNoTracking()
            .GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var users = _db.Users.IgnoreQueryFilters(TenantFilter).AsNoTracking();
        var orders = _db.Orders.IgnoreQueryFilters(TenantFilter).AsNoTracking();

        return new PlatformStatsDto(
            Enum.GetValues<TenantStatus>().ToDictionary(
                s => s.ToString(), s => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0),
            await users.CountAsync(u => u.TenantId == null, ct),
            await users.CountAsync(u => u.TenantId != null && (u.Role == Roles.TenantAdmin || u.Role == Roles.TenantStaff), ct),
            await _db.Customers.IgnoreQueryFilters(TenantFilter).CountAsync(ct),
            await _db.Products.IgnoreQueryFilters(TenantFilter).CountAsync(ct),
            await orders.CountAsync(ct),
            await orders.CountAsync(o => o.CreatedAt >= recentSince, ct));
    }

    // ========================================================================
    // إيراد المنصّة عبر المتاجر (C11) — التجميع كلّه في SQL كما في لوحة المتجر: متجران أو ألفان
    // يكلّفان الاستعلام نفسه، ولا تُحمَّل صفوف طلبات إلى الذاكرة.
    //
    // تعريف "الطلب المحسوب" هو تعريف لوحة المتجر نفسه (مُثبَّت ومدفوع)، وتكراره هنا مقصود وموضعه
    // معروف: لو اختلف التعريفان لأعطت المنصّة رقماً لا يجده التاجر في لوحته، وهي أسوأ أنواع
    // التناقض — رقمان صحيحان كلٌّ بتعريفه، ولا أحد يعرف أيّهما.
    //
    // والعملة تأتي من **المتجر** لا من الطلب: الطلب يجمّد عملته، لكنّ التجميع لكل متجر وعملته
    // واحدة (BR-TEN-18 يمنع تغييرها بعد أول نشاط تجاري)، فالمصدران يتّفقان والمتجر أرخصهما.
    // ========================================================================
    public async Task<PlatformRevenueDto> GetRevenueAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var counted = _db.Orders.IgnoreQueryFilters(TenantFilter).AsNoTracking()
            .Where(o => o.PlacedAt != null && o.PlacedAt >= from && o.PlacedAt < to
                        && (o.Status == Souq.Domain.Enums.OrderStatus.Paid || o.Status == Souq.Domain.Enums.OrderStatus.Shipped
                            || o.Status == Souq.Domain.Enums.OrderStatus.Delivered));

        var perStore = await counted
            .GroupBy(o => o.TenantId)
            .Select(g => new { TenantId = g.Key, Revenue = g.Sum(o => o.PlacedTotal), Orders = g.Count() })
            .ToListAsync(ct);

        // أسماء المتاجر وعملاتها في استعلام واحد للمتاجر التي باعت فقط.
        var ids = perStore.Select(r => r.TenantId).ToList();
        var stores = await _db.Tenants.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Slug, t.Name, t.Currency })
            .ToListAsync(ct);

        var byStore = perStore
            .Join(stores, r => r.TenantId, t => t.Id, (r, t) =>
                new StoreRevenueDto(t.Id, t.Slug, t.Name, t.Currency, r.Revenue, r.Orders))
            .OrderByDescending(r => r.Revenue)
            .ThenBy(r => r.TenantId)
            .ToList();

        var totals = byStore
            .GroupBy(r => r.Currency, StringComparer.Ordinal)
            .Select(g => new CurrencyTotalDto(g.Key, g.Sum(r => r.Revenue), g.Sum(r => r.Orders)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();

        return new PlatformRevenueDto(from, to, totals, byStore);
    }

    private sealed record TenantRow(
        int Id, string Name, string Slug, TenantStatus Status, string Currency, string DefaultCulture,
        string? PrimaryHost, int DomainCount, DateTime CreatedAt);
}
