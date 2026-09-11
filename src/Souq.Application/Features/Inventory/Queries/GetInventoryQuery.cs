using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Inventory.Queries;

// جرد المخزون (شاشة المدير): المنتجات النشطة، الأقلّ مخزوناً أولاً — مرقّم (كان يعيد الكل).
public record GetInventoryQuery(int Page = 1, int PageSize = 50)
    : IRequest<PaginatedList<InventoryItemDto>>, IPagedQuery;

public class GetInventoryHandler : IRequestHandler<GetInventoryQuery, PaginatedList<InventoryItemDto>>
{
    private readonly IInventoryQueries _inventory;
    public GetInventoryHandler(IInventoryQueries inventory) => _inventory = inventory;

    public Task<PaginatedList<InventoryItemDto>> Handle(GetInventoryQuery q, CancellationToken ct) =>
        _inventory.ListAsync(PageRequest.From(q), ct);
}
