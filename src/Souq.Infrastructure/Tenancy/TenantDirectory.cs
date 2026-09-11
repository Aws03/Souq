using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Stores;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Tenancy;

// ============================================================================
// TenantDirectory — تنفيذ ITenantDirectory: استعلام إسقاط صغير على Tenants/TenantDomains (جداول
// منصّة بلا مرشّح مستأجر — هي ما يُعرِّف المستأجر) خلف ذاكرة مؤقتة في العملية. اللقطة تحمل الوحدات
// المفعّلة فيُفرض إغلاق وحدة معطّلة بلا استعلام لكل طلب.
// ============================================================================
internal sealed class TenantDirectory : ITenantDirectory
{
    private readonly AppDbContext _db;
    private readonly TenantDirectoryCache _cache;

    public TenantDirectory(AppDbContext db, TenantDirectoryCache cache)
    {
        _db = db; _cache = cache;
    }

    public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken ct = default)
    {
        var normalized = TenantDomain.TryNormalizeHost(host);
        return normalized is null
            ? Task.FromResult<TenantInfo?>(null)
            : _cache.GetOrLoadAsync($"host:{normalized}",
                () => Project(_db.Tenants.Where(t => t.Domains.Any(d => d.Host == normalized))).FirstOrDefaultAsync(ct));
    }

    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        var normalized = slug?.Trim().ToLowerInvariant() ?? "";
        return normalized.Length is 0 or > Tenant.SlugMaxLength
            ? Task.FromResult<TenantInfo?>(null)
            : _cache.GetOrLoadAsync($"slug:{normalized}",
                () => Project(_db.Tenants.Where(t => t.Slug == normalized)).FirstOrDefaultAsync(ct));
    }

    public Task<TenantInfo?> FindByIdAsync(int tenantId, CancellationToken ct = default) =>
        _cache.GetOrLoadAsync($"id:{tenantId}",
            () => Project(_db.Tenants.Where(t => t.Id == tenantId)).FirstOrDefaultAsync(ct));

    public void Invalidate() => _cache.Invalidate();

    private static IQueryable<TenantInfo> Project(IQueryable<Tenant> tenants) =>
        tenants.AsNoTracking().Select(t => new TenantInfo(
            t.Id, t.Slug, t.Name, t.Status, t.Currency, t.DefaultCulture, t.TimeZone,
            StoreModules.Parse(EF.Property<string>(t, "_modules"))));
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
