using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// طرق شحن متجر السياق (المرحلة 12) — مرشّح المستأجر يحصر القراءة في المتجر.
public class ShippingMethodRepository : RepositoryBase<ShippingMethod>, IShippingMethodRepository
{
    public ShippingMethodRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<ShippingMethod>> ListAsync(bool activeOnly, CancellationToken ct = default) =>
        await Db.ShippingMethods.Where(m => !activeOnly || m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync(ct);
}
