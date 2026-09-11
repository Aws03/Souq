using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مفضّلة عميل في متجر السياق (المرحلة 13) — مرشّح المستأجر يحصر القراءة في المتجر.
public class WishlistRepository : RepositoryBase<WishlistItem>, IWishlistRepository
{
    public WishlistRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<WishlistItem>> ListForCustomerAsync(int customerId, CancellationToken ct = default) =>
        await Db.WishlistItems.Where(w => w.CustomerId == customerId)
            .OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id).ToListAsync(ct);

    public Task<WishlistItem?> FindAsync(int customerId, int productId, CancellationToken ct = default) =>
        Db.WishlistItems.FirstOrDefaultAsync(w => w.CustomerId == customerId && w.ProductId == productId, ct);

    public Task<int> CountForCustomerAsync(int customerId, CancellationToken ct = default) =>
        Db.WishlistItems.CountAsync(w => w.CustomerId == customerId, ct);
}
