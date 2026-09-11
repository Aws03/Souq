using Microsoft.EntityFrameworkCore;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Repositories;

// كل بحث مُرشَّح بالنطاق (مرشّح Tenant على ITenantOrPlatformOwned): البريد نفسه في متجرين حسابان.
public class UserRepository : RepositoryBase<User>, IUserRepository
{
    public UserRepository(AppDbContext db) : base(db) { }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        return Db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
    }

    // القاعدة تحفظ تجزئة الرمز فقط — نجزّئ الرمز الوارد بقاعدة الكيان نفسها ونبحث بها.
    public Task<User?> GetByResetTokenAsync(string token, CancellationToken ct = default)
    {
        var hash = User.HashToken(token);
        return Db.Users.FirstOrDefaultAsync(u => u.PasswordResetTokenHash == hash, ct);
    }

    public Task<User?> GetByVerificationTokenAsync(string token, CancellationToken ct = default)
    {
        var hash = User.HashToken(token);
        return Db.Users.FirstOrDefaultAsync(u => u.EmailVerificationTokenHash == hash, ct);
    }

    public Task<int> CountActiveByRoleAsync(string role, CancellationToken ct = default) =>
        Db.Users.CountAsync(u => u.Role == role && u.Status == UserStatus.Active && u.PasswordHash != "", ct);

    public async Task<IReadOnlyList<int>> ListActiveIdsByRolesAsync(IReadOnlyCollection<string> roles, CancellationToken ct = default) =>
        await Db.Users.Where(u => roles.Contains(u.Role) && u.Status == UserStatus.Active && u.PasswordHash != "")
            .OrderBy(u => u.Id).Select(u => u.Id).ToListAsync(ct);
}

// المتجر بنطاقاته (جدول منصّة: لا مرشّح). لا حذف: المتاجر تُؤرشف (سجلّها المالي والتدقيقي يبقى).
public class TenantRepository : ITenantRepository
{
    private readonly AppDbContext _db;
    public TenantRepository(AppDbContext db) => _db = db;

    public Task<Tenant?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Tenants.Include(t => t.Domains).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(Tenant entity, CancellationToken ct = default) => await _db.Tenants.AddAsync(entity, ct);

    public void Remove(Tenant entity) =>
        throw new InvalidOperationException("المتاجر لا تُحذف — تُؤرشف (Tenant.Archive).");

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        _db.Tenants.AnyAsync(t => t.Slug == slug, ct);

    public Task<bool> HostTakenAsync(string host, CancellationToken ct = default) =>
        _db.TenantDomains.AnyAsync(d => d.Host == host, ct);
}

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;
    public RefreshTokenRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var hash = User.HashToken(token);
        return _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    public async Task<IReadOnlyList<RefreshToken>> ListActiveInFamilyAsync(Guid familyId, CancellationToken ct = default) =>
        await _db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null && t.UsedAt == null).ToListAsync(ct);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveForUserAsync(int userId, CancellationToken ct = default) =>
        await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null && t.UsedAt == null).ToListAsync(ct);
}
