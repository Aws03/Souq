using Souq.Application.Common.Security;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Baskets;

// ============================================================================
// سلة المتصل (المرحلة 8). عميل ⇒ سلته، ويُدمج فيها سلة زائر يحمل رمزها ثم تُحذف ويُمسح الرمز (دخول من جهاز كانت فيه
// سلة زائر). غير ذلك (زائر، أو موظّف بلا ملف شراء) ⇒ سلة رمز الزائر. رمز لا يطابق سلة حيّة (منتهية، مدموجة، أو من متجر
// آخر — البحث مُرشَّح بالمتجر) يُعامل كغيابه ويُمسح. لا حفظ هنا: المعالج يحفظ بعد تعديله.
// ============================================================================
public sealed class BasketResolver
{
    private readonly IBasketRepository _baskets;
    private readonly ICurrentUser _currentUser;
    private readonly BasketSettings _settings;
    private readonly TimeProvider _clock;

    public BasketResolver(IBasketRepository baskets, ICurrentUser currentUser, BasketSettings settings, TimeProvider clock)
    {
        _baskets = baskets; _currentUser = currentUser; _settings = settings; _clock = clock;
    }

    // سلة المتصل إن وُجدت. لا إنشاء إلا لاستقبال دمج سلة زائر في سلة عميل لم تكن له.
    public async Task<ResolvedBasket> ResolveAsync(string? guestToken, CancellationToken ct)
    {
        var guest = await LiveGuestBasketAsync(guestToken, ct);
        var clearToken = guestToken is not null ? GuestCookieAction.Clear : GuestCookieAction.None;

        if (_currentUser.CustomerId is not int customerId)
            return guest is not null
                ? new ResolvedBasket(guest, GuestCookieAction.None, guestToken)
                : new ResolvedBasket(null, clearToken, null);

        var basket = await _baskets.GetForCustomerAsync(customerId, ct);
        if (basket is not null && basket.IsExpired(Now))
            basket.Clear(ExpiryFor(basket));

        if (guest is not null)
        {
            if (basket is null)
            {
                basket = Basket.ForCustomer(customerId, ExpiryFor(guest: false));
                await _baskets.AddAsync(basket, ct);
            }
            basket.MergeFrom(guest, ExpiryFor(basket));
            _baskets.Remove(guest);
        }
        return new ResolvedBasket(basket, clearToken, null);
    }

    // سلة للكتابة: القائمة، أو جديدة للمتصل — للعميل سلته، ولغيره سلة زائر برمز جديد يُرسَل في ملف التعريف.
    public async Task<ResolvedBasket> EnsureAsync(ResolvedBasket resolved, CancellationToken ct)
    {
        if (resolved.Basket is not null) return resolved;

        if (_currentUser.CustomerId is int customerId)
        {
            var basket = Basket.ForCustomer(customerId, ExpiryFor(guest: false));
            await _baskets.AddAsync(basket, ct);
            return resolved with { Basket = basket };
        }

        var token = GuestBasketTokens.New();
        var guestBasket = Basket.ForGuest(GuestBasketTokens.Hash(token), ExpiryFor(guest: true));
        await _baskets.AddAsync(guestBasket, ct);
        return new ResolvedBasket(guestBasket, GuestCookieAction.Set, token);
    }

    public DateTime ExpiryFor(Basket basket) => ExpiryFor(basket.IsGuest);

    private DateTime ExpiryFor(bool guest) => Now + _settings.LifetimeFor(guest);

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private async Task<Basket?> LiveGuestBasketAsync(string? token, CancellationToken ct)
    {
        if (!GuestBasketTokens.IsWellFormed(token)) return null;

        var basket = await _baskets.GetForGuestAsync(GuestBasketTokens.Hash(token!), ct);
        if (basket is null || !basket.IsExpired(Now)) return basket;

        _baskets.Remove(basket);   // منتهية: تُحذف مع أول حفظ، ورمزها يُمسح أو يُستبدل
        return null;
    }
}

public sealed record ResolvedBasket(Basket? Basket, GuestCookieAction Cookie, string? GuestToken)
{
    public BasketResult Read(BasketDto view) => new(view, Cookie);

    // بعد كتابة ناجحة: سلة الزائر تمدّ عمر ملف تعريفها مع عمرها، وسلة العميل تمسح رمز زائر إن حُمل.
    public BasketResult Written(BasketDto view) => Basket is { IsGuest: true } guest
        ? new BasketResult(view, GuestCookieAction.Set, GuestToken, guest.ExpiresAt)
        : new BasketResult(view, Cookie);
}
