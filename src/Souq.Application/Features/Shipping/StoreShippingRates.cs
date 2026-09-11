using Souq.Application.Features.Shipping.Contracts;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Shipping;

// المزوّد الافتراضي لأسعار الشحن (المرحلة 12): طرق المتجر المفعّلة — جدول أسعار يعرّفه المدير. طريقة بعملة غير عملة السلة
// (غيّر المتجر عملته) لا تُعرض بدل أن تسعّر بعملة خاطئة. الترتيب: ترتيب المدير، ثم الأرخص.
public sealed class StoreShippingRates : IShippingRateProvider
{
    private readonly IShippingMethodRepository _methods;
    public StoreShippingRates(IShippingMethodRepository methods) => _methods = methods;

    public async Task<ShippingQuote> QuoteAsync(Money goods, string? country, CancellationToken ct)
    {
        var active = await _methods.ListAsync(activeOnly: true, ct);
        var options = active
            .Where(m => m.Price.Currency == goods.Currency && m.Serves(country))
            .Select(m => (Method: m, Cost: m.RateFor(goods)))
            .OrderBy(x => x.Method.SortOrder).ThenBy(x => x.Cost.Amount).ThenBy(x => x.Method.Id)
            .Select(x => new ShippingOption(x.Method.Id, x.Method.Name, x.Cost, x.Method.Carrier, x.Method.TrackingUrlTemplate,
                x.Method.MinDays, x.Method.MaxDays))
            .ToList();
        return new ShippingQuote(options, StoreShips: active.Count > 0);
    }
}
