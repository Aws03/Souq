using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Inventory.Queries;

// المنتجات المنخفضة المخزون — للتنبيه على لوحة التحكّم. الشارة تحتاج TotalCount فقط
// (pageSize=1)، والقائمة السريعة صفحة صغيرة.
public record GetLowStockQuery(int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<InventoryItemDto>>, IPagedQuery;

public class GetLowStockHandler : IRequestHandler<GetLowStockQuery, PaginatedList<InventoryItemDto>>
{
    private readonly IInventoryQueries _inventory;
    public GetLowStockHandler(IInventoryQueries inventory) => _inventory = inventory;

    public Task<PaginatedList<InventoryItemDto>> Handle(GetLowStockQuery q, CancellationToken ct) =>
        _inventory.ListLowStockAsync(PageRequest.From(q), ct);
}
