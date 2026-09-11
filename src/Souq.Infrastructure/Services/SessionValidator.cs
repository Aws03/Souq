using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Identity;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Services;

// ============================================================================
// SessionValidator — يطابق ختم الأمان في التوكن مع ختم الحساب الحالي (وأنه فعّال). القراءة مُرشَّحة
// بنطاق الطلب (حساب هذا المتجر أو المنصّة فقط)، ومخزَّنة 30 ثانية بمفتاح يحمل النطاق: تغيير كلمة
// المرور أو التعطيل يسري فوراً على هذه النسخة (Forget) وخلال 30 ثانية على غيرها.
// ============================================================================
internal sealed class SessionValidator : ISessionValidator
{
    private readonly AppDbContext _db;
    private readonly SessionStampCache _cache;
    private readonly ITenantContext _tenancy;

    public SessionValidator(AppDbContext db, SessionStampCache cache, ITenantContext tenancy)
    {
        _db = db; _cache = cache; _tenancy = tenancy;
    }

    public async Task<bool> IsCurrentAsync(int userId, string securityStamp, CancellationToken ct = default)
    {
        var key = Key(userId);
        if (!_cache.TryGet(key, out var current))
        {
            current = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.Status == UserStatus.Active)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(ct);
            _cache.Set(key, current);
        }

        return current is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(securityStamp ?? ""));
    }

    public void Forget(int userId) => _cache.Remove(Key(userId));

    private string Key(int userId) => $"{_tenancy.Tenant?.Id.ToString() ?? "platform"}:{userId}";
}

// ذاكرة الأختام (Singleton) بحدّ حجم — مفتاح لكل حساب نشط؛ "لا ختم" (معطّل/محذوف) يُخزَّن أيضاً.
public sealed class SessionStampCache : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 50_000 });

    internal bool TryGet(string key, out string? stamp)
    {
        if (_cache.TryGetValue(key, out Entry? entry) && entry is not null)
        {
            stamp = entry.Stamp;
            return true;
        }
        stamp = null;
        return false;
    }

    internal void Set(string key, string? stamp) =>
        _cache.Set(key, new Entry(stamp), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });

    internal void Remove(string key) => _cache.Remove(key);

    public void Dispose() => _cache.Dispose();

    private sealed record Entry(string? Stamp);
}
