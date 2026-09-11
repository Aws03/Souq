using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة لمفضّلة العميل (المرحلة 13). العرض بأسعار الكتالوج الحيّة عبر IWishlistQueries (ADR-0008).
public interface IWishlistRepository : IRepository<WishlistItem>
{
    // الأحدث أولاً — للدمج ولمحو العميل (حقّ الحذف: المفضّلة بيانات شخصية تُحذف معه).
    Task<IReadOnlyList<WishlistItem>> ListForCustomerAsync(int customerId, CancellationToken ct = default);

    Task<WishlistItem?> FindAsync(int customerId, int productId, CancellationToken ct = default);

    Task<int> CountForCustomerAsync(int customerId, CancellationToken ct = default);
}
