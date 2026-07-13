using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// سجلّ حركة المخزون: نضيف حركة، ونجلب تاريخ منتج (الأحدث أولاً). لا حذف/تعديل —
// السجلّ للقراءة والإضافة فقط بطبيعته (سجلّ تدقيق).
public interface IStockMovementRepository
{
    Task AddAsync(StockMovement movement, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovement>> GetByProductAsync(int productId, CancellationToken ct = default);
}
