using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مستودع حركات المخزون: إضافة وقراءة تاريخ منتج فقط (سجلّ تدقيق لا يُعدَّل).
public class StockMovementRepository : IStockMovementRepository
{
    private readonly AppDbContext _db;
    public StockMovementRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(StockMovement movement, CancellationToken ct = default)
        => await _db.StockMovements.AddAsync(movement, ct);

    // الأحدث أولاً؛ نكسر تعادل الطابع الزمني بالمعرّف تنازلياً كي يبقى ترتيب
    // الحركات المتزامنة (نفس اللحظة) ثابتاً بين الاستدعاءات.
    public async Task<IReadOnlyList<StockMovement>> GetByProductAsync(int productId, CancellationToken ct = default)
        => await _db.StockMovements
                    .Where(m => m.ProductId == productId)
                    .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                    .ToListAsync(ct);
}
