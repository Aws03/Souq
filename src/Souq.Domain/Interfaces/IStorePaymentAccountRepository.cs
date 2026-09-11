using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// حساب بوّابة متجر السياق (المرحلة 11) — صفّ واحد على الأكثر لكل متجر.
public interface IStorePaymentAccountRepository : IRepository<StorePaymentAccount>
{
    Task<StorePaymentAccount?> GetAsync(CancellationToken ct = default);
}
