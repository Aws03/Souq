using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// طرق شحن متجر السياق (المرحلة 12) — جدول صغير يُقرأ كاملاً ومرتّباً.
public interface IShippingMethodRepository : IRepository<ShippingMethod>
{
    Task<IReadOnlyList<ShippingMethod>> ListAsync(bool activeOnly, CancellationToken ct = default);
}
