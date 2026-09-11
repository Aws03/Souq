using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Shipping.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Baskets.Pricing;

// ============================================================================
// تنفيذ IPricing. المنتجات تُحمَّل دفعة واحدة (لا استعلام لكل سطر)، والمستودع مُرشَّح بالمتجر: منتج متجر آخر "غير
// موجود" هنا كما في أي مكان. الشحن (المرحلة 12) من IShippingRateProvider: طرق المتجر التي تخدم العنوان بسعرها للإجمالي
// بعد الخصم، ومشكلته (طريقة مطلوبة أو غير متاحة) نتيجة في العرض لا فشل. الضريبة صفر صريح — لا نموذج ضريبة (قرار منتج
// مفتوح P-06) — ومكانها في الخطّ ثابت. قواعد الكوبون كلها من الكيان، وحدّ العميل (المرحلة 10) من استخداماته الفعّالة.
// ============================================================================
public sealed class PricingService : IPricing
{
    private readonly IProductRepository _products;
    private readonly ICouponRepository _coupons;
    private readonly ICouponRedemptionRepository _redemptions;
    private readonly IShippingRateProvider _shipping;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public PricingService(
        IProductRepository products, ICouponRepository coupons, ICouponRedemptionRepository redemptions,
        IShippingRateProvider shipping, ITenantContext tenant, TimeProvider clock)
    {
        _products = products; _coupons = coupons; _redemptions = redemptions; _shipping = shipping; _tenant = tenant; _clock = clock;
    }

    public async Task<PriceQuote> QuoteAsync(
        IReadOnlyList<PricingLine> lines, string? couponCode, int? customerId, ShippingRequest? shipping, CancellationToken ct)
    {
        var store = _tenant.RequireTenant();
        var zero = Money.Zero(store.Currency);

        // (1) الأسطر بأسعار الكتالوج الحيّة.
        var products = (await _products.GetManyAsync(lines.Select(l => l.ProductId).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id);
        var priced = lines
            .Select(line => products.TryGetValue(line.ProductId, out var product)
                ? Price(product, line.Quantity, store.DefaultCulture)
                : Missing(line, zero))
            .ToList();

        // (2) الفرعي: الأسطر القابلة للبيع وحدها.
        var subtotal = priced.Where(l => l.Sellable).Aggregate(zero, (sum, l) => sum.Add(l.LineTotal));

        // (3) الخصم.
        var (coupon, discount) = await DiscountAsync(couponCode, subtotal, customerId, store, ct);

        // (4) الشحن (المرحلة 12): بالإجمالي بعد الخصم (حدّ المجانية يُقاس به).
        var goods = subtotal.Subtract(discount);
        var delivery = await ShippingAsync(goods, shipping, ct);
        var shippingCost = delivery.Selected?.Cost ?? zero;

        // (5) الضريبة: صفر صريح (انظر أعلاه).
        var tax = zero;

        // (6) الإجمالي.
        var total = goods.Add(shippingCost).Add(tax);
        return new PriceQuote(store.Currency, priced, subtotal, coupon, discount, shippingCost, tax, total, delivery);
    }

    // الطرق المتاحة للعنوان والمختارة منها. متجر بطرق شحن يلزمه اختيار طريقة تخدم العنوان (الدفع يرفض بالرمز)؛ متجر بلا طرق
    // شحنه مجاني بلا اختيار.
    private async Task<ShippingOutcome> ShippingAsync(Money goods, ShippingRequest? request, CancellationToken ct)
    {
        var quote = await _shipping.QuoteAsync(goods, request?.Country, ct);
        var selected = request?.MethodId is int id ? quote.Options.FirstOrDefault(o => o.MethodId == id) : null;

        if (request?.MethodId is not null && selected is null)
            return new ShippingOutcome(quote.Options, null, quote.StoreShips, "ShippingMethodUnavailable",
                "طريقة الشحن المختارة لا تخدم هذا العنوان أو لم تعد متاحة");
        if (selected is null && quote.StoreShips)
            return quote.Options.Count == 0
                ? new ShippingOutcome(quote.Options, null, true, "ShippingNotAvailable", "لا طريقة شحن تخدم هذا العنوان")
                : new ShippingOutcome(quote.Options, null, true, "ShippingMethodRequired", "اختر طريقة الشحن");
        return new ShippingOutcome(quote.Options, selected, quote.StoreShips, null, null);
    }

    private async Task<(CouponOutcome?, Money)> DiscountAsync(
        string? code, Money subtotal, int? customerId, TenantInfo store, CancellationToken ct)
    {
        var none = Money.Zero(subtotal.Currency);
        if (string.IsNullOrWhiteSpace(code)) return (null, none);
        code = code.Trim();

        // وحدة الكوبونات معطّلة لهذا المتجر (D-11): الواجهة تُخفي الحقل، والخادم يرفض على أي حال.
        if (!store.HasModule(StoreModules.Promotions))
            return (new CouponOutcome(code, false, "ModuleDisabled", "الكوبونات غير مفعّلة في هذا المتجر"), none);

        var coupon = await _coupons.GetByCodeAsync(code, ct);
        if (coupon is null)
            return (new CouponOutcome(code, false, "CouponNotFound", "رمز الكوبون غير صحيح"), none);

        var customerUses = customerId is int id ? await _redemptions.CountActiveAsync(coupon.Id, id, ct) : 0;
        try
        {
            coupon.EnsureUsable(subtotal, _clock.GetUtcNow().UtcDateTime, customerUses);
        }
        catch (InvalidCouponException ex)
        {
            return (new CouponOutcome(coupon.Code, false, ex.Code, ex.Message), none);
        }

        return (new CouponOutcome(coupon.Code, true, null, null), coupon.CalculateDiscount(subtotal));
    }

    private static PricedLine Price(Product product, int quantity, string culture) => new(
        product.Id, product.DefaultVariant.Id, product.NameIn(culture),
        product.Translations.ToDictionary(t => t.Culture, t => t.Name), product.PrimaryImageUrl,
        product.Price, quantity, product.Price.Multiply(quantity), product.IsActive);

    private static PricedLine Missing(PricingLine line, Money zero) => new(
        line.ProductId, 0, "", new Dictionary<string, string>(), null, zero, line.Quantity, zero, Sellable: false);
}
