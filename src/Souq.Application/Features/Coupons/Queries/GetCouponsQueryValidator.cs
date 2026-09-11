using FluentValidation;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Coupons.Queries;

public class GetCouponsQueryValidator : AbstractValidator<GetCouponsQuery>
{
    public GetCouponsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PagingRules.MaxPageSize);
    }
}
