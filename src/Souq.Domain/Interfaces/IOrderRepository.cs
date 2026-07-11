using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

public interface IOrderRepository : IRepository<Order>
{
    // نحتاج جلب الطلب مع أسطره معاً (التجمّع كاملاً).
    Task<Order?> GetWithItemsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByCustomerAsync(int customerId, CancellationToken ct = default);

    // كل الطلبات مرقّمة (لشاشة طلبات المدير) — الأحدث أولاً.
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default);
}
