using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Inventory.Queries;

// سجلّ حركة مخزون منتج واحد (درج التاريخ) — الأحدث أولاً، مرقّم: السجلّ دفتري لا يُحذف
// منه شيء، فبلا ترقيم يكبر الردّ مع كل بيع إلى الأبد.
public record GetStockMovementsQuery(int ProductId, int Page = 1, int PageSize = 50)
    : IRequest<PaginatedList<StockMovementDto>>, IPagedQuery;

public class GetStockMovementsHandler
    : IRequestHandler<GetStockMovementsQuery, PaginatedList<StockMovementDto>>
{
    private readonly IInventoryQueries _inventory;
    public GetStockMovementsHandler(IInventoryQueries inventory) => _inventory = inventory;

    public Task<PaginatedList<StockMovementDto>> Handle(GetStockMovementsQuery q, CancellationToken ct) =>
        _inventory.ListMovementsAsync(q.ProductId, PageRequest.From(q), ct);
}
