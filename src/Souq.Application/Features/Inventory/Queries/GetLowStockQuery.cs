using MediatR;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Inventory.Queries;

// المنتجات المنخفضة المخزون فقط — للتنبيه على لوحة التحكّم (بادج) والقائمة السريعة.
public record GetLowStockQuery : IRequest<IReadOnlyList<InventoryItemDto>>;

public class GetLowStockHandler : IRequestHandler<GetLowStockQuery, IReadOnlyList<InventoryItemDto>>
{
    private readonly IProductRepository _products;
    public GetLowStockHandler(IProductRepository products) => _products = products;

    public async Task<IReadOnlyList<InventoryItemDto>> Handle(GetLowStockQuery q, CancellationToken ct)
    {
        var items = await _products.GetLowStockAsync(ct);
        return items.Select(p => new InventoryItemDto(
            p.Id, p.NameAr, p.NameEn, p.ImageUrl, p.Category?.Name,
            p.StockQuantity, p.LowStockThreshold, p.IsLowStock)).ToList();
    }
}
