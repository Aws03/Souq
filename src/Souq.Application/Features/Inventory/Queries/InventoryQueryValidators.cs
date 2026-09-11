using FluentValidation;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Inventory.Queries;

public class GetInventoryQueryValidator : PagedQueryValidator<GetInventoryQuery>;

public class GetLowStockQueryValidator : PagedQueryValidator<GetLowStockQuery>;

public class GetStockMovementsQueryValidator : PagedQueryValidator<GetStockMovementsQuery>
{
    public GetStockMovementsQueryValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
    }
}
