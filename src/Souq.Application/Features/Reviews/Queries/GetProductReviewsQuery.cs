using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Reviews.Queries;

public record ReviewDto(int Id, string CustomerName, int Rating, string Comment, DateTime CreatedAt);

// الإجماليات (المرحلة 13) من التقييمات المعتمدة وحدها: العدد والمتوسط والتوزيع على النجوم (5 ⇒ 1، كل نجمة حاضرة ولو صفراً).
public record RatingCountDto(int Rating, int Count);
public record ProductReviewsDto(
    IReadOnlyList<ReviewDto> Items, int TotalCount, int Page, int PageSize, double AverageRating,
    IReadOnlyList<RatingCountDto> Distribution);

public record GetProductReviewsQuery(int ProductId, int Page = 1, int PageSize = 10)
    : IRequest<ProductReviewsDto>, IPagedQuery;

// قائمة التقييمات + المتوسط عبر منفذ القراءة: اسم المقيِّم بـ JOIN لا استعلام لكل تقييم (C13).
public class GetProductReviewsHandler : IRequestHandler<GetProductReviewsQuery, ProductReviewsDto>
{
    private readonly IReviewQueries _reviews;
    public GetProductReviewsHandler(IReviewQueries reviews) => _reviews = reviews;

    public Task<ProductReviewsDto> Handle(GetProductReviewsQuery q, CancellationToken ct) =>
        _reviews.ListForProductAsync(q.ProductId, PageRequest.From(q), ct);
}
