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

    public async Task<(IReadOnlyList<Coupon> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var total = await Db.Coupons.CountAsync(ct);
        var items = await Db.Coupons.OrderByDescending(c => c.CreatedAt)
                                    .Skip((page - 1) * pageSize).Take(pageSize)
                                    .ToListAsync(ct);
        return (items, total);
    }
}
