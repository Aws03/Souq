using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

public class CouponRepository : RepositoryBase<Coupon>, ICouponRepository
{
    public CouponRepository(AppDbContext db) : base(db) { }

    public async Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await Db.Coupons.FirstOrDefaultAsync(c => c.Code == normalized, ct);
    }
}
