using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مستودع حركات المخزون: إضافة فقط (سجلّ تدقيق لا يُعدَّل). القراءة في InventoryQueries.
public class StockMovementRepository : IStockMovementRepository
{
    private readonly AppDbContext _db;
    public StockMovementRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(StockMovement movement, CancellationToken ct = default)
        => await _db.StockMovements.AddAsync(movement, ct);
}
