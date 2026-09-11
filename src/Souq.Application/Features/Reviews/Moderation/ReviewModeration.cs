using System.Globalization;
using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Reviews.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Reviews.Moderation;

// ============================================================================
// الإشراف على التقييمات (المرحلة 13، reviews.moderate، ADR-0033): قائمة الإدارة بكل الحالات، والاعتماد والرفض. التقييم من
// متجر السياق وحده — معرّف تقييم متجر آخر ⇒ 404 (مرشّح المستأجر). كل قرار يُدقَّق، ويُحفظ على التقييم نفسه من قرّر ومتى.
// ============================================================================

public sealed record AdminReviewDto(
    int Id, int ProductId, string ProductName, string ProductSlug, int CustomerId, string CustomerName,
    int Rating, string Comment, string Status, DateTime CreatedAt, DateTime? ModeratedAt, string? ModerationNote);

public sealed record ReviewModerationFilter(ReviewStatus? Status, int? ProductId);

public record ListReviewsForModerationQuery(ReviewStatus? Status = null, int? ProductId = null, int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<AdminReviewDto>>, IPagedQuery;

public sealed class ListReviewsForModerationValidator : PagedQueryValidator<ListReviewsForModerationQuery>
{
    public ListReviewsForModerationValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.ProductId).GreaterThan(0).When(x => x.ProductId is not null);
    }
}

public class ListReviewsForModerationHandler : IRequestHandler<ListReviewsForModerationQuery, PaginatedList<AdminReviewDto>>
{
    private readonly IReviewQueries _reviews;
    private readonly ITenantContext _tenant;

    public ListReviewsForModerationHandler(IReviewQueries reviews, ITenantContext tenant)
    {
        _reviews = reviews; _tenant = tenant;
    }

    public Task<PaginatedList<AdminReviewDto>> Handle(ListReviewsForModerationQuery q, CancellationToken ct) =>
        _reviews.ListForModerationAsync(
            new ReviewModerationFilter(q.Status, q.ProductId), PageRequest.From(q), _tenant.RequireTenant().DefaultCulture, ct);
}

public record ApproveReviewCommand(int ReviewId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("review.approved", "Review", ReviewId.ToString(CultureInfo.InvariantCulture));
}

// الملاحظة لا تدخل سجلّ التدقيق: نصّ حرّ قد يقتبس التقييم — تبقى على التقييم نفسه للإدارة وحدها.
public record RejectReviewCommand(int ReviewId, string? Note) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("review.rejected", "Review", ReviewId.ToString(CultureInfo.InvariantCulture));
}

public sealed class RejectReviewValidator : AbstractValidator<RejectReviewCommand>
{
    public RejectReviewValidator() => RuleFor(x => x.Note).MaximumLength(Review.ModerationNoteMaxLength);
}

public class ApproveReviewHandler : IRequestHandler<ApproveReviewCommand, Result>
{
    private readonly IReviewRepository _reviews;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public ApproveReviewHandler(IReviewRepository reviews, ICurrentUser currentUser, TimeProvider clock, IUnitOfWork uow)
    {
        _reviews = reviews; _currentUser = currentUser; _clock = clock; _uow = uow;
    }

    public Task<Result> Handle(ApproveReviewCommand cmd, CancellationToken ct) =>
        ReviewDecision.ApplyAsync(_reviews, _uow, cmd.ReviewId,
            review => review.Approve(_currentUser.RequireUserId(), _clock.GetUtcNow().UtcDateTime), ct);
}

public class RejectReviewHandler : IRequestHandler<RejectReviewCommand, Result>
{
    private readonly IReviewRepository _reviews;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RejectReviewHandler(IReviewRepository reviews, ICurrentUser currentUser, TimeProvider clock, IUnitOfWork uow)
    {
        _reviews = reviews; _currentUser = currentUser; _clock = clock; _uow = uow;
    }

    public Task<Result> Handle(RejectReviewCommand cmd, CancellationToken ct) =>
        ReviewDecision.ApplyAsync(_reviews, _uow, cmd.ReviewId,
            review => review.Reject(_currentUser.RequireUserId(), _clock.GetUtcNow().UtcDateTime, cmd.Note), ct);
}

internal static class ReviewDecision
{
    public static async Task<Result> ApplyAsync(
        IReviewRepository reviews, IUnitOfWork uow, int reviewId, Action<Review> decide, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(reviewId, ct);
        if (review is null)
            return Result.Failure(Error.NotFound("التقييم غير موجود"));

        decide(review);
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
