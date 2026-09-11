using FluentValidation;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Queries;

public class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    public GetOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagingRules.MaxPageSize);
    }
}
