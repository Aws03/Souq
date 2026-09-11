using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Coupons.Queries;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// تنفيذ ICouponQueries (ADR-0008): إسقاط بلا تتبّع، الأحدث أولاً مع كاسر تعادل بالمعرّف.
internal sealed class CouponQueries : ICouponQueries
{
    private readonly AppDbContext _db;
    public CouponQueries(AppDbContext db) => _db = db;

    public async Task<PaginatedList<CouponDto>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var rows = await _db.Coupons.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .ToPageAsync(c => new CouponRow(
                c.Id, c.Code, c.Type, c.Value, (decimal?)c.MinOrderAmount!.Amount,
                c.ExpiresAt, c.MaxUses, c.UsedCount, c.IsActive), page, ct);

        // اسم النوع نصاً في الذاكرة — لا نعتمد على ترجمة ToString() لـ enum داخل SQL.
        return rows.Map(r => new CouponDto(
            r.Id, r.Code, r.Type.ToString(), r.Value, r.MinOrderAmount,
            r.ExpiresAt, r.MaxUses, r.UsedCount, r.IsActive));
    }

    private sealed record CouponRow(
        int Id, string Code, DiscountType Type, decimal Value, decimal? MinOrderAmount,
        DateTime? ExpiresAt, int? MaxUses, int UsedCount, bool IsActive);
}
