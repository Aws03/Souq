using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;

namespace Souq.Application.Features.Inventory.Queries;

// المنتجات المنخفضة المخزون — للتنبيه على لوحة التحكّم. الشارة تحتاج TotalCount فقط
// (pageSize=1)، والقائمة السريعة صفحة صغيرة.
public record GetLowStockQuery(int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<InventoryItemDto>>, IPagedQuery;

public class GetLowStockHandler : IRequestHandler<GetLowStockQuery, PaginatedList<InventoryItemDto>>
{
    private readonly IInventoryQueries _inventory;
    private readonly ITenantContext _tenant;

    public GetLowStockHandler(IInventoryQueries inventory, ITenantContext tenant)
    {
        _inventory = inventory; _tenant = tenant;
    }

    public Task<PaginatedList<InventoryItemDto>> Handle(GetLowStockQuery q, CancellationToken ct) =>
        _inventory.ListLowStockAsync(PageRequest.From(q), _tenant.RequireTenant().DefaultCulture, ct);
}
