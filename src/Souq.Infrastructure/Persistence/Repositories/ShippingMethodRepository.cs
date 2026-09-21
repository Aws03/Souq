using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Application.Common.Models;

namespace Souq.Infrastructure.Persistence.Repositories;

// طرق شحن متجر السياق (المرحلة 12) — مرشّح المستأجر يحصر القراءة في المتجر.
public class ShippingMethodRepository : RepositoryBase<ShippingMethod>, IShippingMethodRepository
{
    public ShippingMethodRepository(AppDbContext db) : base(db) { }

    // F-21: تُعاد كاملةً بحكم شكلها (قائمةُ شحنٍ ناقصة تُخفي خياراً عن مشترٍ)، بسقفٍ يحرس القراءة.
    // لا متجر حقيقي يقترب من ألف طريقة شحن؛ السقف لمتجرٍ شاذّ أو بيانات مُولَّدة، لا للتصفّح.
    public async Task<IReadOnlyList<ShippingMethod>> ListAsync(bool activeOnly, CancellationToken ct = default) =>
        await Db.ShippingMethods.Where(m => !activeOnly || m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Take(PagingRules.MaxUnpagedItems).ToListAsync(ct);
}
