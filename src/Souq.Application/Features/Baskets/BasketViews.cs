using Souq.Application.Common.Security;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Baskets;

// عرض السلة: الأسعار والمجاميع من IPricing لحظة الطلب (بحدّ الكوبون للعميل المعروف — المرحلة 10)، والمتاح من Inventory
// للعرض (لا حجز). ReadyForCheckout يلخّص ما سيقبله الدفع: كل سطر قابل للبيع وكميته متاحة، والكوبون (إن طُلب) مقبول.
public sealed class BasketViews
{
    private readonly IPricing _pricing;
    private readonly IStockAvailability _availability;
    private readonly ICurrentUser _currentUser;

    public BasketViews(IPricing pricing, IStockAvailability availability, ICurrentUser currentUser)
    {
        _pricing = pricing; _availability = availability; _currentUser = currentUser;
    }

    public Task<BasketDto> BuildAsync(Basket? basket, string? couponCode, CancellationToken ct) =>
        BuildAsync(basket, couponCode, shipping: null, ct);

    // shipping (المرحلة 12): الطريقة المختارة ودولة العنوان لتسعير الدفع؛ بدونهما تُعرض الطرق غير المقيَّدة بدول وحدها.
    public async Task<BasketDto> BuildAsync(Basket? basket, string? couponCode, ShippingRequest? shipping, CancellationToken ct)
    {
        List<BasketLine> lines = basket?.Lines.OrderBy(l => l.Id).ToList() ?? [];
        var quote = await _pricing.QuoteAsync(
            lines.Select(l => new PricingLine(l.ProductId, l.Quantity, l.VariantId)).ToList(), couponCode, _currentUser.CustomerId, shipping, ct);

        var variantIds = lines.Select(l => l.VariantId).Distinct().ToList();
        IReadOnlyDictionary<int, int> available = variantIds.Count == 0
            ? new Dictionary<int, int>()
            : await _availability.AvailableAsync(variantIds, ct);

        // أسطر التسعير بترتيب المدخل نفسه — تُقرن بأسطر السلة واحداً لواحد.
        var dtoLines = lines.Zip(quote.Lines, (line, priced) => new BasketLineDto(
                line.ProductId, line.VariantId, priced.Name,
                priced.Names.ToDictionary(n => n.Key, n => new BasketText(n.Value)), priced.ImageUrl,
                priced.UnitPrice.Amount, line.Quantity, priced.LineTotal.Amount, priced.Sellable,
                priced.Sellable ? Math.Max(available.GetValueOrDefault(line.VariantId), 0) : 0))
            .ToList();

        var ready = dtoLines.Count > 0
                    && dtoLines.All(l => l.Sellable && l.Quantity <= l.Available)
                    && quote.Coupon is not { Applied: false };

        return new BasketDto(dtoLines, dtoLines.Sum(l => l.Quantity), quote.Currency,
            quote.Subtotal.Amount, quote.Discount.Amount, quote.Shipping.Amount, quote.Tax.Amount, quote.Total.Amount,
            quote.Coupon is { } c ? new BasketCouponDto(c.Code, c.Applied, c.ErrorCode, c.Message) : null,
            ready, basket?.ExpiresAt, Shipping(quote.ShippingOutcome));
    }

    private static BasketShippingDto? Shipping(ShippingOutcome? outcome) => outcome is null ? null : new BasketShippingDto(
        outcome.Options.Select(o => new ShippingOptionDto(o.MethodId, o.Name, o.Cost.Amount, o.Carrier, o.MinDays, o.MaxDays)).ToList(),
        outcome.Selected?.MethodId, outcome.Required, outcome.ErrorCode, outcome.Message);
}
