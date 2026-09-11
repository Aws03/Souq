using FluentValidation;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Reviews.Queries;

public class GetProductReviewsQueryValidator : PagedQueryValidator<GetProductReviewsQuery>
{
    public GetProductReviewsQueryValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
    }
}
