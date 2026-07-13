using MediatR;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Inventory.Queries;

// سجلّ حركة مخزون منتج واحد (درج التاريخ) — الأحدث أولاً.
public record GetStockMovementsQuery(int ProductId) : IRequest<IReadOnlyList<StockMovementDto>>;

public class GetStockMovementsHandler
    : IRequestHandler<GetStockMovementsQuery, IReadOnlyList<StockMovementDto>>
{
    private readonly IStockMovementRepository _movements;
    public GetStockMovementsHandler(IStockMovementRepository movements) => _movements = movements;

    public async Task<IReadOnlyList<StockMovementDto>> Handle(GetStockMovementsQuery q, CancellationToken ct)
    {
        var items = await _movements.GetByProductAsync(q.ProductId, ct);
        return items.Select(m => new StockMovementDto(
            m.Id, m.Type.ToString(), m.QuantityChange, m.NewQuantity, m.Note, m.CreatedAt)).ToList();
    }
}
