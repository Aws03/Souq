using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// سجلّ حركة المخزون: إضافة فقط — سجلّ تدقيق لا يُعدَّل ولا يُحذف. قراءته (مرقّمة، الأحدث
// أولاً) عبر IInventoryQueries (ADR-0008).
public interface IStockMovementRepository
{
    Task AddAsync(StockMovement movement, CancellationToken ct = default);
}
