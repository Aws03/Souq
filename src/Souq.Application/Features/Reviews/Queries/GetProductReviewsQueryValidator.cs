using FluentValidation;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Reviews.Queries;

public class GetProductReviewsQueryValidator : AbstractValidator<GetProductReviewsQuery>
{
    public GetProductReviewsQueryValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagingRules.MaxPageSize);
    }
}
