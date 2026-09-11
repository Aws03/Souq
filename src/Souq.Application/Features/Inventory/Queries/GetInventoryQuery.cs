using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;

namespace Souq.Application.Features.Inventory.Queries;

// جرد المخزون (شاشة المدير): المنتجات النشطة، الأقلّ مخزوناً أولاً — مرقّم (كان يعيد الكل).
public record GetInventoryQuery(int Page = 1, int PageSize = 50)
    : IRequest<PaginatedList<InventoryItemDto>>, IPagedQuery;

public class GetInventoryHandler : IRequestHandler<GetInventoryQuery, PaginatedList<InventoryItemDto>>
{
    private readonly IInventoryQueries _inventory;
    private readonly ITenantContext _tenant;

    public GetInventoryHandler(IInventoryQueries inventory, ITenantContext tenant)
    {
        _inventory = inventory; _tenant = tenant;
    }

    public Task<PaginatedList<InventoryItemDto>> Handle(GetInventoryQuery q, CancellationToken ct) =>
        _inventory.ListAsync(PageRequest.From(q), _tenant.RequireTenant().DefaultCulture, ct);
}
