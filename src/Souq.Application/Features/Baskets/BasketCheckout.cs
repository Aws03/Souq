using Souq.Application.Features.Baskets.Contracts;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Baskets;

// تنفيذ IBasketCheckout (المرحلة 9). سلة منتهية لا تُدفع منها (كأنها فارغة). الاستهلاك بالكيان نفسه (SetQuantity) فتبقى
// قواعده، ويمدّ عمر السلة كأي تعديل.
public sealed class BasketCheckout : IBasketCheckout
{
    private readonly IBasketRepository _baskets;
    private readonly BasketSettings _settings;
    private readonly TimeProvider _clock;

    public BasketCheckout(IBasketRepository baskets, BasketSettings settings, TimeProvider clock)
    {
        _baskets = baskets; _settings = settings; _clock = clock;
    }

    public async Task<IReadOnlyList<PricingLine>> LinesForCustomerAsync(int customerId, CancellationToken ct)
    {
        var basket = await _baskets.GetForCustomerAsync(customerId, ct);
        if (basket is null || basket.IsExpired(_clock.GetUtcNow().UtcDateTime)) return [];
        return basket.Lines.OrderBy(l => l.Id).Select(l => new PricingLine(l.ProductId, l.Quantity)).ToList();
    }

    public async Task ConsumeAsync(int customerId, IReadOnlyList<PricingLine> purchased, CancellationToken ct)
    {
        var basket = await _baskets.GetForCustomerAsync(customerId, ct);
        if (basket is null) return;

        var expiresAt = _clock.GetUtcNow().UtcDateTime + _settings.LifetimeFor(guest: false);
        foreach (var product in purchased.GroupBy(p => p.ProductId))
        {
            if (basket.LineFor(product.Key) is not { } line) continue;
            basket.SetQuantity(line.VariantId, Math.Max(line.Quantity - product.Sum(p => p.Quantity), 0), expiresAt);
        }
    }
}
