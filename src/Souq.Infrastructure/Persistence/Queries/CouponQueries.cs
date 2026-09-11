using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Coupons.Queries;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// تنفيذ ICouponQueries (ADR-0008): إسقاط بلا تتبّع، الأحدث أولاً مع كاسر تعادل بالمعرّف. استخدامات الكوبون برقم الطلب
// واسم العميل باستعلامات مرتبطة (المرحلة 10).
internal sealed class CouponQueries : ICouponQueries
{
    private readonly AppDbContext _db;
    public CouponQueries(AppDbContext db) => _db = db;

    public async Task<PaginatedList<CouponDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var rows = await _db.Coupons.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .ToPageAsync(c => new CouponRow(
                c.Id, c.Code, c.Type, c.Value, (decimal?)c.MinOrderAmount!.Amount, c.StartsAt,
                c.ExpiresAt, c.MaxUses, c.MaxUsesPerCustomer, c.UsedCount, c.IsActive), page, ct);

        // اسم النوع نصاً في الذاكرة — لا نعتمد على ترجمة ToString() لـ enum داخل SQL.
        return rows.Map(r => new CouponDto(
            r.Id, r.Code, r.Type.ToString(), r.Value, r.MinOrderAmount, r.StartsAt,
            r.ExpiresAt, r.MaxUses, r.MaxUsesPerCustomer, r.UsedCount, r.IsActive));
    }

    public async Task<PaginatedList<CouponRedemptionDto>?> ListRedemptionsAsync(int couponId, PageRequest page, CancellationToken ct)
    {
        if (!await _db.Coupons.AnyAsync(c => c.Id == couponId, ct)) return null;

        var rows = await _db.CouponRedemptions.AsNoTracking()
            .Where(r => r.CouponId == couponId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToPageAsync(r => new RedemptionRow(
                r.OrderId,
                _db.Orders.Where(o => o.Id == r.OrderId).Select(o => o.OrderNumber).FirstOrDefault(),
                r.CustomerId,
                _db.Customers.Where(c => c.Id == r.CustomerId).Select(c => c.FullName).FirstOrDefault(),
                r.Discount.Amount, r.Discount.Currency, r.Status, r.CreatedAt), page, ct);

        return rows.Map(r => new CouponRedemptionDto(
            r.OrderId, r.OrderNumber, r.CustomerId, r.CustomerName, r.Discount, r.Currency, r.Status.ToString(), r.CreatedAt));
    }

    private sealed record CouponRow(
        int Id, string Code, DiscountType Type, decimal Value, decimal? MinOrderAmount, DateTime? StartsAt,
        DateTime? ExpiresAt, int? MaxUses, int? MaxUsesPerCustomer, int UsedCount, bool IsActive);

    private sealed record RedemptionRow(
        int OrderId, int OrderNumber, int CustomerId, string? CustomerName, decimal Discount, string Currency,
        CouponRedemptionStatus Status, DateTime CreatedAt);
}
