using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Reviews.Moderation;
using Souq.Application.Features.Reviews.Queries;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ IReviewQueries (ADR-0008). اسم المقيِّم بـ JOIN في استعلام الصفحة نفسه — كان المعالج يجلب كل عميل على حدة (N+1،
// Phase 0 C13). العرض العام ثلاثة استعلامات ثابتة: التوزيع (ومنه العدد والمتوسط — تجميع بخمسة صفوف على الأكثر) وعدد الصفحة
// والصفحة. الاسم الأول فقط يُعرض للعامة (خصوصية): الاقتطاع في الذاكرة لأن Split لا يُترجم لـ SQL. المشرف يرى الاسم كاملاً.
// ============================================================================
internal sealed class ReviewQueries : IReviewQueries
{
    private const string AnonymousReviewer = "عميل";

    private readonly AppDbContext _db;
    public ReviewQueries(AppDbContext db) => _db = db;

    public async Task<ProductReviewsDto> ListForProductAsync(int productId, PageRequest page, CancellationToken ct)
    {
        // ============================================================================
        // التقييمات تتبع ظهور المنتج نفسه (M15).
        //
        // كان الشرط `ProductId` و`Approved` وحدهما، بلا أي صلة بظهور المنتج. فمنتجٌ سُحب من الواجهة
        // — مسوّدة، أو مؤرشف، أو فئته معطّلة — يردّ `GET /api/products/{id}` بـ404 بينما يظلّ
        // `GET /api/products/{id}/reviews` (نقطة عامّة بلا تسجيل دخول) يُعيد أسماء المقيّمين ونصوص
        // تعليقاتهم وتوزيع التقييمات. فمن يمرّ على المعرّفات يعرف أيُّها سُحب — ويقرأ محتواه وإشارةً
        // عن حجم مبيعاته — بمجرّد أن يردّ أحد المسارين 404 والآخر بيانات.
        //
        // والشرط هو نفسه شرط `CatalogQueries.VisibleProducts` حرفياً: منتجٌ نشط في فئة مفعّلة.
        // ============================================================================
        var reviews = _db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .Where(r => _db.Products.Any(p => p.Id == r.ProductId
                                              && p.Status == ProductStatus.Active
                                              && p.Category!.IsActive));

        var counts = await reviews.GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);
        var average = total == 0 ? 0 : counts.Sum(c => c.Rating * c.Count) / (double)total;
        var distribution = Enumerable.Range(1, 5).Reverse()
            .Select(star => new RatingCountDto(star, counts.FirstOrDefault(c => c.Rating == star)?.Count ?? 0))
            .ToList();

        var rows = await reviews
            .Join(_db.Customers.AsNoTracking(), r => r.CustomerId, c => c.Id, (r, c) => new { Review = r, c.FullName })
            .OrderByDescending(x => x.Review.CreatedAt).ThenByDescending(x => x.Review.Id)
            .ToPageAsync(x => new ReviewRow(x.Review.Id, x.FullName, x.Review.Rating, x.Review.Comment, x.Review.CreatedAt),
                page, ct);

        var items = rows.Items
            .Select(r => new ReviewDto(r.Id, FirstName(r.FullName), r.Rating, r.Comment, r.CreatedAt))
            .ToList();

        return new ProductReviewsDto(items, rows.TotalCount, page.Page, page.PageSize, Math.Round(average, 1), distribution);
    }

    public async Task<PaginatedList<AdminReviewDto>> ListForModerationAsync(
        ReviewModerationFilter filter, PageRequest page, string culture, CancellationToken ct)
    {
        var reviews = _db.Reviews.AsNoTracking();
        if (filter.Status is { } status) reviews = reviews.Where(r => r.Status == status);
        if (filter.ProductId is { } productId) reviews = reviews.Where(r => r.ProductId == productId);

        var rows = await reviews
            .Join(_db.Customers.AsNoTracking(), r => r.CustomerId, c => c.Id, (r, c) => new { Review = r, c.FullName })
            .Join(_db.Products.AsNoTracking(), x => x.Review.ProductId, p => p.Id, (x, p) => new
            {
                x.Review, x.FullName, p.Slug,
                ProductName = p.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                              ?? p.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
            })
            .OrderByDescending(x => x.Review.CreatedAt).ThenByDescending(x => x.Review.Id)
            .ToPageAsync(x => new ModerationRow(
                x.Review.Id, x.Review.ProductId, x.ProductName, x.Slug, x.Review.CustomerId, x.FullName,
                x.Review.Rating, x.Review.Comment, x.Review.Status, x.Review.CreatedAt, x.Review.ModeratedAt, x.Review.ModerationNote),
                page, ct);

        return new PaginatedList<AdminReviewDto>(
            rows.Items.Select(r => new AdminReviewDto(
                r.Id, r.ProductId, r.ProductName ?? r.ProductSlug, r.ProductSlug, r.CustomerId,
                string.IsNullOrWhiteSpace(r.FullName) ? AnonymousReviewer : r.FullName,
                r.Rating, r.Comment, r.Status.ToString(), r.CreatedAt, r.ModeratedAt, r.ModerationNote)).ToList(),
            rows.TotalCount, page.Page, page.PageSize);
    }

    private static string FirstName(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? AnonymousReviewer : fullName.Trim().Split(' ')[0];

    private sealed record ReviewRow(int Id, string FullName, int Rating, string Comment, DateTime CreatedAt);

    private sealed record ModerationRow(
        int Id, int ProductId, string? ProductName, string ProductSlug, int CustomerId, string? FullName, int Rating, string Comment,
        ReviewStatus Status, DateTime CreatedAt, DateTime? ModeratedAt, string? ModerationNote);
}
