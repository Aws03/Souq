using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Entities;

// ============================================================================
// WishlistItem — منتج في مفضّلة عميل (المرحلة 13، وحدة Shopping، ADR-0033): معرّفا العميل والمنتج داخل المتجر فقط؛ الاسم
// والسعر والصورة تُقرأ حيّةً من الكتالوج عند العرض (لا لقطة — المفضّلة ليست فاتورة). منتج واحد مرة واحدة لكل عميل.
// ============================================================================
public class WishlistItem : Entity, ITenantOwned
{
    public const int MaxItemsPerCustomer = 200;

    public int TenantId { get; private set; }
    public int CustomerId { get; private set; }
    public int ProductId { get; private set; }

    // سقف المفضّلة لكل عميل: القائمة تُعرض كاملة بلا ترقيم، والدمج من المتصفّح لا يضخّها بلا حدّ.
    public static bool HasRoom(int currentCount) => currentCount < MaxItemsPerCustomer;

    private WishlistItem() { }

    public WishlistItem(int customerId, int productId)
    {
        if (customerId <= 0 || productId <= 0)
            throw new InvalidCustomerDataException("عنصر المفضّلة يخصّ عميلاً ومنتجاً محفوظَين");
        CustomerId = customerId;
        ProductId = productId;
    }
}
