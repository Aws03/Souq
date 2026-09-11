using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// استخدامات الكوبونات لجهة الكتابة (المرحلة 10). Reset ينسى الكوبونات والاستخدامات المتتبَّعة بعد تعارض (نمط InventoryRepository).
public class CouponRedemptionRepository : RepositoryBase<CouponRedemption>, ICouponRedemptionRepository
{
    public CouponRedemptionRepository(AppDbContext db) : base(db) { }

    public Task<CouponRedemption?> GetForOrderAsync(int orderId, CancellationToken ct = default) =>
        Db.CouponRedemptions.FirstOrDefaultAsync(r => r.OrderId == orderId, ct);

    public Task<int> CountActiveAsync(int couponId, int customerId, CancellationToken ct = default) =>
        Db.CouponRedemptions.CountAsync(
            r => r.CouponId == couponId && r.CustomerId == customerId && r.Status != CouponRedemptionStatus.Released, ct);

    public Task<bool> AnyForCouponAsync(int couponId, CancellationToken ct = default) =>
        Db.CouponRedemptions.AnyAsync(r => r.CouponId == couponId, ct);

    public void Reset()
    {
        foreach (var entry in Db.ChangeTracker.Entries().Where(e => e.Entity is Coupon or CouponRedemption).ToList())
            entry.State = EntityState.Detached;
    }
}
