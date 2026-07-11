using MediatR;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Reviews.Queries;

public record ReviewDto(int Id, string CustomerName, int Rating, string Comment, DateTime CreatedAt);
public record ProductReviewsDto(
    IReadOnlyList<ReviewDto> Items, int TotalCount, int Page, int PageSize, double AverageRating);

public record GetProductReviewsQuery(int ProductId, int Page = 1, int PageSize = 10) : IRequest<ProductReviewsDto>;

public class GetProductReviewsHandler : IRequestHandler<GetProductReviewsQuery, ProductReviewsDto>
{
    private readonly IReviewRepository _reviews;
    private readonly ICustomerRepository _customers;

    public GetProductReviewsHandler(IReviewRepository reviews, ICustomerRepository customers)
    {
        _reviews = reviews; _customers = customers;
    }

    public async Task<ProductReviewsDto> Handle(GetProductReviewsQuery q, CancellationToken ct)
    {
        var (items, total, average) = await _reviews.GetByProductAsync(q.ProductId, q.Page, q.PageSize, ct);

        // قائمة صغيرة (pageSize محدود) — جلب اسم العميل لكل تقييم على حدة مقبول
        // هنا، لا يستحق تعقيد استعلام join فقط لتوفير بضع رحلات قاعدة بيانات.
        var dtos = new List<ReviewDto>();
        foreach (var r in items)
        {
            var customer = await _customers.GetByIdAsync(r.CustomerId, ct);
            var name = customer?.FullName?.Split(' ')[0] ?? "عميل";
            dtos.Add(new ReviewDto(r.Id, name, r.Rating, r.Comment, r.CreatedAt));
        }

        return new ProductReviewsDto(dtos, total, q.Page, q.PageSize, Math.Round(average, 1));
    }
}
