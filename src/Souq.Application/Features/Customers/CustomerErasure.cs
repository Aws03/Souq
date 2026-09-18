using Souq.Application.Common.Security;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Application.Features.Auth.Contracts;

namespace Souq.Application.Features.Customers;

// ============================================================================
// محو عميل (حقّ الحذف، المرحلة 7) — مسار واحد للعميل نفسه وللإدارة. الملف والحساب يُجرَّدان من البيانات الشخصية
// ويتوقّفان في حفظ واحد، ورموز التجديد تُلغى، وذاكرة ختم الجلسة تُنسى فتسقط توكنات الوصول القائمة فوراً.
// الطلبات والتقييمات تبقى (سجلّ مالي يُحتفَظ به) مشيرةً إلى ملف لم يعد يعرّف أحداً — لقطة عنوان الشحن على الطلب
// تبقى لأنها جزء من الفاتورة. السلة (المرحلة 8) والمفضّلة (المرحلة 13) نيّة شراء لا سجلّ، فتُحذفان.
// ============================================================================
public sealed class CustomerErasure
{
    private readonly IAccountLifecycle _accounts;
    private readonly IBasketRepository _baskets;
    private readonly IWishlistRepository _wishlist;
    private readonly TimeProvider _clock;

    public CustomerErasure(
        IAccountLifecycle accounts, IBasketRepository baskets, IWishlistRepository wishlist, TimeProvider clock)
    {
        _accounts = accounts; _baskets = baskets; _wishlist = wishlist; _clock = clock;
    }

    public async Task EraseAsync(Customer customer, CancellationToken ct)
    {
        customer.Erase(_clock.GetUtcNow().UtcDateTime);
        if (await _baskets.GetForCustomerAsync(customer.Id, ct) is { } basket)
            _baskets.Remove(basket);
        foreach (var item in await _wishlist.ListForCustomerAsync(customer.Id, ct))
            _wishlist.Remove(item);

        // ثم الحساب — وهو من يحفظ (IAccountLifecycle.EraseAsync)، فما جُهِّز أعلاه يُودَع معه في حفظٍ
        // واحد: المحو ذرّةٌ واحدة كما كان قبل العقد. إبطال الجلسات ونسيان ختمها صارا داخل Identity،
        // فلم تبقَ هذه الوحدة تُعدّل تجمّع حسابٍ لا تملكه (TD-03، M9).
        await _accounts.EraseAsync(customer.UserId, ct);
    }
}
