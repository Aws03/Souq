using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Reviews.Queries;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ IReviewQueries (ADR-0008). اسم المقيِّم بـ JOIN في استعلام الصفحة نفسه — كان
// المعالج يجلب كل عميل على حدة (N+1، Phase 0 C13). ثلاثة استعلامات ثابتة: العدد والمتوسط
// والصفحة. الاسم الأول فقط يُعرض (خصوصية): الاقتطاع في الذاكرة لأن Split لا يُترجم لـ SQL.
// ============================================================================
internal sealed class ReviewQueries : IReviewQueries
{
    private const string AnonymousReviewer = "عميل";

    private readonly AppDbContext _db;
    public ReviewQueries(AppDbContext db) => _db = db;

    public async Task<ProductReviewsDto> ListForProductAsync(int productId, PageRequest page, CancellationToken ct)
    {
        var reviews = _db.Reviews.AsNoTracking().Where(r => r.ProductId == productId);

        var average = await reviews.AverageAsync(r => (double?)r.Rating, ct) ?? 0;

        var rows = await reviews
            .Join(_db.Customers.AsNoTracking(), r => r.CustomerId, c => c.Id, (r, c) => new { Review = r, c.FullName })
            .OrderByDescending(x => x.Review.CreatedAt).ThenByDescending(x => x.Review.Id)
            .ToPageAsync(x => new ReviewRow(x.Review.Id, x.FullName, x.Review.Rating, x.Review.Comment, x.Review.CreatedAt),
                page, ct);

        var items = rows.Items
            .Select(r => new ReviewDto(r.Id, FirstName(r.FullName), r.Rating, r.Comment, r.CreatedAt))
            .ToList();

        return new ProductReviewsDto(items, rows.TotalCount, page.Page, page.PageSize, Math.Round(average, 1));
    }

    private static string FirstName(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? AnonymousReviewer : fullName.Trim().Split(' ')[0];

    private sealed record ReviewRow(int Id, string FullName, int Rating, string Comment, DateTime CreatedAt);
}
