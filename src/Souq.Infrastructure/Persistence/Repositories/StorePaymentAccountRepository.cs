using Microsoft.EntityFrameworkCore;
using Souq.Domain.Interfaces;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Repositories;

// حساب بوّابة متجر السياق (المرحلة 11) — مرشّح المستأجر يحصر القراءة في صفّه.
public class StorePaymentAccountRepository : RepositoryBase<StorePaymentAccount>, IStorePaymentAccountRepository
{
    public StorePaymentAccountRepository(AppDbContext db) : base(db) { }

    public Task<StorePaymentAccount?> GetAsync(CancellationToken ct = default) =>
        Db.StorePaymentAccounts.FirstOrDefaultAsync(ct);
}
