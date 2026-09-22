using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Stores;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Tenancy;

// ============================================================================
// TenantDirectory — تنفيذ ITenantDirectory: استعلام إسقاط صغير على Tenants/TenantDomains (جداول
// منصّة بلا مرشّح مستأجر — هي ما يُعرِّف المستأجر) خلف ذاكرة مؤقتة في العملية. اللقطة تحمل الوحدات
// المفعّلة فيُفرض إغلاق وحدة معطّلة بلا استعلام لكل طلب.
//
// **الجواب الواحد يُحسب هنا** (C1، ADR-0047 §4): الوحدات الفعّالة = ما يسمح به عقد المتجر (استحقاقات
// إصدار خطته + استثناءات الدعم السارية) ∩ ما فعّلته المنصّة له. لا فحص ثانٍ في مكان آخر: كل من
// يسأل يسأل TenantInfo.HasModule كما كان. ثلاثة جداول إضافية تُقرأ في **نفس** الرحلة عبر استعلامات
// فرعية، وتُخزَّن مع اللقطة 60 ثانية كسائرها.
//
// الاشتراكات والاستثناءات جداول منصّة **بمفتاح متجر** بلا مرشّح: الشرط `== t.Id` أدناه هو عزلها.
// ============================================================================
internal sealed class TenantDirectory : ITenantDirectory
{
    private readonly AppDbContext _db;
    private readonly TenantDirectoryCache _cache;
    private readonly ICacheSignals _signals;
    private readonly TimeProvider _clock;
    private readonly ILogger<TenantDirectory> _log;

    public TenantDirectory(
        AppDbContext db, TenantDirectoryCache cache, ICacheSignals signals,
        TimeProvider clock, ILogger<TenantDirectory> log)
    {
        _db = db; _cache = cache; _signals = signals; _clock = clock; _log = log;
    }

    public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken ct = default)
    {
        var normalized = TenantDomain.TryNormalizeHost(host);
        return normalized is null
            ? Task.FromResult<TenantInfo?>(null)
            : _cache.GetOrLoadAsync($"host:{normalized}",
                () => FirstAsync(_db.Tenants.Where(t => t.Domains.Any(d => d.Host == normalized)), ct));
    }

    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        var normalized = slug?.Trim().ToLowerInvariant() ?? "";
        return normalized.Length is 0 or > Tenant.SlugMaxLength
            ? Task.FromResult<TenantInfo?>(null)
            : _cache.GetOrLoadAsync($"slug:{normalized}",
                () => FirstAsync(_db.Tenants.Where(t => t.Slug == normalized), ct));
    }

    public Task<TenantInfo?> FindByIdAsync(int tenantId, CancellationToken ct = default) =>
        _cache.GetOrLoadAsync($"id:{tenantId}", () => FirstAsync(_db.Tenants.Where(t => t.Id == tenantId), ct));

    // النشط والموقوف: حجوزات المتجر الموقوف لا يُصفّيها شيء آخر (R-24) — انظر ITenantDirectory للسبب كاملاً.
    public async Task<IReadOnlyList<TenantInfo>> ListForBackgroundSweepsAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var rows = await Project(_db.Tenants
                .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Suspended)
                .OrderBy(t => t.Id), now)
            .ToListAsync(ct);
        return rows.Select(ToTenantInfo).ToList();
    }

    // ============================================================================
    // الإبطال: **محلّيّاً أوّلاً ثمّ تُنشَر الإشارة** (C4، [ADR-0057](0057)).
    //
    // والترتيبُ مقصود: النشرُ أوّلاً كان سيترك نافذةً — قصيرة لكنّها حقيقية — تخدم فيها هذه
    // النسخةُ نفسُها لقطةً تعرف أنّها قديمة، وهي النسخةُ الوحيدة التي تعرف ذلك يقيناً.
    //
    // وفشلُ النشر يُترك ليرتفع: الاستدعاءُ يقع بعد الحفظ، فالتغييرُ ملتزَمٌ أصلاً — والصمتُ عن
    // فشلٍ هنا يعني بقيّةَ النسخ على القديم حتى تنتهي المدّة، بلا أن يعرف أحد.
    // ============================================================================
    public async Task InvalidateAsync(CancellationToken ct = default)
    {
        _cache.Invalidate();
        await _signals.BumpAsync(CacheSignal.TenantDirectory, ct);
    }

    private async Task<TenantInfo?> FirstAsync(IQueryable<Tenant> tenants, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var row = await Project(tenants, now).FirstOrDefaultAsync(ct);
        return row is null ? null : ToTenantInfo(row);
    }

    // ما تحتاجه اللقطة من القاعدة، بمداخله الثلاثة غير مُدمَجة بعد: الدمج قاعدةُ مجال (Entitlements)
    // لا تعبير SQL، وإبقاؤه في المجال هو ما يجعله مُختبَراً في Souq.Domain.Tests.
    private IQueryable<TenantSnapshotRow> Project(IQueryable<Tenant> tenants, DateTime utcNow) =>
        tenants.AsNoTracking().Select(t => new TenantSnapshotRow(
            t.Id, t.Slug, t.Name, t.Status, t.Currency, t.DefaultCulture, t.TimeZone,
            EF.Property<string>(t, "_modules"),
            // الاشتراك السارٍ وحده يمنح؛ الملغى لا يمنح شيئاً. الخطة المتقاعدة **تظلّ تمنح** لمشتركها:
            // التقاعد يمنع اشتراكاً جديداً ولا يسحب ما اشتُرك عليه.
            _db.Subscriptions
                .Where(s => s.TenantId == t.Id && s.Status == SubscriptionStatus.Active)
                .SelectMany(s => _db.PlanEntitlements.Where(e => EF.Property<int>(e, "PlanId") == s.PlanId)
                    .Select(e => e.Entitlement))
                .ToList(),
            _db.EntitlementOverrides
                .Where(o => o.TenantId == t.Id && o.RevokedAtUtc == null && o.ExpiresAtUtc > utcNow)
                .Select(o => o.Entitlement)
                .ToList(),
            // حدود الخطة السارية (C2، ADR-0054). من الاشتراك نفسه ومن إصداره نفسه الذي جاءت منه
            // الاستحقاقات: عقدٌ واحد يُقرأ مرّة، لا سؤالان قد يجيبان عن إصدارين مختلفين.
            // **لا استثناءات على الحدود**: الاستثناء يمنح قدرة ولا يرفع سقفاً — رفعُ السقف تغييرُ
            // خطة، وله مساره (C-14 سمّى الاستحقاق وحده).
            _db.Subscriptions
                .Where(s => s.TenantId == t.Id && s.Status == SubscriptionStatus.Active)
                .SelectMany(s => _db.PlanLimits.Where(l => EF.Property<int>(l, "PlanId") == s.PlanId))
                .Select(l => new PlanLimitRow(l.Name, l.Value))
                .ToList()));

    private TenantInfo ToTenantInfo(TenantSnapshotRow row)
    {
        var enabled = StoreModules.Parse(row.EnabledModules);

        // القراءة المتسامحة تبقى متسامحة (مفتاح أُزيل من المنتج لا يُسقط متجراً) لكنها لم تعد **صامتة**:
        // مفتاح مجهول في عمود المتجر أو في خطته يعني انحرافاً بين المنتج والبيانات، وهو ما لا يُكتشف أبداً
        // ما لم يُقَل. التجاهل نفسه يفشل مغلقاً — المفتاح المجهول لا يُمنح.
        WarnAboutUnknown(row.Id, "وحدات المتجر", StoreModules.Unknown(row.EnabledModules));
        WarnAboutUnknown(row.Id, "استحقاقات الخطة", row.PlanEntitlements.Where(k => !Entitlements.IsKnown(k)).ToList());
        WarnAboutUnknown(row.Id, "حدود الخطة", row.PlanLimits.Where(l => !LimitNames.IsKnown(l.Name))
            .Select(l => l.Name).ToList());

        var granted = Entitlements.Granted(row.PlanEntitlements, row.ActiveOverrides);
        return new TenantInfo(row.Id, row.Slug, row.Name, row.Status, row.Currency, row.DefaultCulture, row.TimeZone,
            Entitlements.Effective(granted, enabled),
            // المجهول يُسقَط لا يُرفَض (كسائر القراءة هنا): حدٌّ باسم أُزيل من المنتج لا يجوز أن
            // يُسقط متجراً — ولا أن يُفرض، إذ لا قاعدة عدّ له. وإسقاطه يعني "غير مقيَّد" لا "صفر"،
            // وهو الاتجاه الوحيد الذي لا يوقف تاجراً بسبب انحرافٍ في بيانات المنصّة (ADR-0054).
            // والأخير يفوز عند التكرار المستحيل: الفهرس الفريد (PlanId, Name) يمنعه في القاعدة.
            row.PlanLimits.Where(l => LimitNames.IsKnown(l.Name))
                .ToDictionary(l => l.Name, l => l.Value, StringComparer.Ordinal));
    }

    private void WarnAboutUnknown(int tenantId, string source, IReadOnlyList<string> unknown)
    {
        if (unknown.Count == 0) return;
        _log.LogWarning("متجر {TenantId}: {Source} تحمل مفاتيح مجهولة تُتجاهَل: {Keys}",
            tenantId, source, string.Join(',', unknown));
    }

    // صفّ وسيط بين SQL والمجال — لا يعبر حدود Infrastructure.
    private sealed record TenantSnapshotRow(
        int Id, string Slug, string Name, TenantStatus Status, string Currency, string DefaultCulture, string TimeZone,
        string EnabledModules, List<string> PlanEntitlements, List<string> ActiveOverrides,
        List<PlanLimitRow> PlanLimits);

    private sealed record PlanLimitRow(string Name, int Value);
}

// إعداد الواجهة لمتجر (IStoreConfiguration) في الذاكرة نفسها: يُبطَل مع الدليل عند كل تعديل على متجر.
internal sealed class StoreConfiguration : IStoreConfiguration
{
    private readonly AppDbContext _db;
    private readonly TenantDirectoryCache _cache;

    public StoreConfiguration(AppDbContext db, TenantDirectoryCache cache)
    {
        _db = db; _cache = cache;
    }

    public Task<StorefrontConfigDto?> GetStorefrontAsync(int tenantId, CancellationToken ct) =>
        _cache.GetOrLoadAsync($"storefront:{tenantId}", async () =>
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            return tenant is null ? null : StoreSettingsMapper.ToStorefront(tenant);
        });
}

// ============================================================================
// ذاكرة الدليل (Singleton). قرارات مقصودة:
//   • مثيل MemoryCache خاص بحدّ حجم: المضيف ترويسة يتحكّم بها المهاجم — بلا حدّ، آلاف المضيفين
//     العشوائيين تملأ الذاكرة (والنتائج السلبية تُخزَّن لمدّة أقصر كي لا يُضرَب SQL بكل طلب).
//   • صلاحية قصيرة (60 ث) + Invalidate بزيادة "الجيل" (يُبطل كل المفاتيح دفعة واحدة): تغييرات
//     المتاجر نادرة، وإيقاف متجر يسري على هذه النسخة فوراً وعلى غيرها خلال دقيقة.
//   • **الاستحقاق يرث المدّة نفسها** (C1): تغيير خطة أو سحب استثناء يستدعي Invalidate فيسري فوراً
//     هنا وخلال دقيقة على غيرها؛ أمّا **انتهاء** استثناء بنفسه فلا يستدعيه أحد، فيبقى ممنوحاً حتى
//     60 ثانية بعد وقته. مقبول لاستثناءٍ أقصاه تسعون يوماً، ومذكور كي لا يُكتشف مفاجأةً — والإبطال
//     عبر النسخ هو ما يعالجه C4.
// ============================================================================
public sealed class TenantDirectoryCache : IDisposable
{
    private static readonly TimeSpan HitLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MissLifetime = TimeSpan.FromSeconds(15);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private long _generation;

    internal async Task<T?> GetOrLoadAsync<T>(string key, Func<Task<T?>> load) where T : class
    {
        var cacheKey = $"{Interlocked.Read(ref _generation)}:{key}";
        if (_cache.TryGetValue(cacheKey, out Entry? cached) && cached is not null)
            return (T?)cached.Value;

        var loaded = await load();
        _cache.Set(cacheKey, new Entry(loaded), new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = loaded is null ? MissLifetime : HitLifetime,
        });
        return loaded;
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    public void Dispose() => _cache.Dispose();

    // غلاف كي تُخزَّن "لا نتيجة" أيضاً (MemoryCache لا يميّز القيمة null عن الغياب).
    private sealed record Entry(object? Value);
}
