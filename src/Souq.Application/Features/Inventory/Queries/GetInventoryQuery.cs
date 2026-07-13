using MediatR;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Inventory.Queries;

// جرد كامل للمخزون (شاشة المدير): كل المنتجات النشطة، الأقلّ مخزوناً أولاً.
public record GetInventoryQuery : IRequest<IReadOnlyList<InventoryItemDto>>;

public class GetInventoryHandler : IRequestHandler<GetInventoryQuery, IReadOnlyList<InventoryItemDto>>
{
    private readonly IProductRepository _products;
    public GetInventoryHandler(IProductRepository products) => _products = products;

    public async Task<IReadOnlyList<InventoryItemDto>> Handle(GetInventoryQuery q, CancellationToken ct)
    {
        var items = await _products.GetInventoryAsync(ct);
        return items.Select(p => new InventoryItemDto(
            p.Id, p.NameAr, p.NameEn, p.ImageUrl, p.Category?.Name,
            p.StockQuantity, p.LowStockThreshold, p.IsLowStock)).ToList();
    }
}
