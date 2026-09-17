using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Baskets.Pricing;
using Souq.Application.Features.Shipping.Contracts;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Baskets;

// خطّ التسعير الواحد (المرحلة 8): أسعار الكتالوج الحيّة، الأسطر غير القابلة للبيع خارج المجموع، الشحن والضريبة صفر صريح،
// والكوبون نتيجة في العرض لا استثناء — وحدّ العميل للكوبون (المرحلة 10). الدفع يستعمل الخطّ نفسه (CreateOrderHandlerTests).
public class PricingServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly ICouponRedemptionRepository _redemptions = Substitute.For<ICouponRedemptionRepository>();
    private Souq.Application.Features.Shipping.Contracts.IShippingRateProvider _shipping = TestShipping.None();

    private PricingService Pricing(ITenantContext? store = null) =>
        new(_products, _coupons, _redemptions, _shipping, store ?? TestTenant.Context(), new FixedClock());

    private void Catalog(params Product[] products) =>
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(products.ToList());

    [Fact]
    public async Task الأسطر_بأسعار_الكتالوج_والشحن_والضريبة_صفر_صريح()
    {
        Catalog(TestCatalog.Product("سماعات", price: 12.5m, id: 1), TestCatalog.Product("شاحن", price: 3m, id: 2));

        var quote = await Pricing().QuoteAsync([new(1, 2), new(2, 1)], null, null, null, CancellationToken.None);

        quote.Lines.Select(l => (l.ProductId, l.VariantId, l.UnitPrice.Amount, l.LineTotal.Amount, l.Sellable))
            .Should().Equal((1, 1, 12.5m, 25m, true), (2, 2, 3m, 3m, true));
        quote.Lines[0].Names.Should().Contain("ar", "سماعات");
        (quote.Subtotal.Amount, quote.Discount.Amount, quote.Shipping.Amount, quote.Tax.Amount, quote.Total.Amount)
            .Should().Be((28m, 0m, 0m, 0m, 28m));
        (quote.Currency, quote.Coupon).Should().Be(("JOD", (CouponOutcome?)null));
    }

    [Fact]
    public async Task غير_المنشور_والغائب_خارج_المجموع_ومعلَّمان()
    {
        var draft = TestCatalog.Product("مسودّة", price: 10m, id: 1);
        draft.ChangeStatus(ProductStatus.Draft);
        Catalog(draft, TestCatalog.Product("منشور", price: 4m, id: 2));

        var quote = await Pricing().QuoteAsync([new(1, 1), new(2, 2), new(99, 1)], null, null, null, CancellationToken.None);

        quote.Lines.Select(l => (l.ProductId, l.Sellable)).Should().Equal((1, false), (2, true), (99, false));
        (quote.Subtotal.Amount, quote.Total.Amount).Should().Be((8m, 8m));
    }

    // R-07: خطّ التسعير هو الحدّ الموثوق للشراء (السلة والدفع يمرّان به) — فالفئة المعطّلة تُخرج منتجها من المجموع هنا.
    [Fact]
    public async Task منتج_في_فئة_معطّلة_غير_قابل_للبيع_وخارج_المجموع()
    {
        Catalog(TestCatalog.Product("مخفي بفئته", price: 30m, id: 1, categoryActive: false),
                TestCatalog.Product("ظاهر", price: 4m, id: 2));

        var quote = await Pricing().QuoteAsync([new(1, 1), new(2, 1)], null, null, null, CancellationToken.None);

        quote.Lines.Select(l => (l.ProductId, l.Sellable)).Should().Equal((1, false), (2, true));
        (quote.Subtotal.Amount, quote.Total.Amount).Should().Be((4m, 4m));
    }

    [Fact]
    public async Task الكوبون_المقبول_يخصم_من_الفرعي()
    {
        Catalog(TestCatalog.Product(price: 50m, id: 1));
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>())
            .Returns(new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null));

        var quote = await Pricing().QuoteAsync([new(1, 2)], " SAVE10 ", null, null, CancellationToken.None);

        quote.Coupon.Should().Be(new CouponOutcome("SAVE10", true, null, null));
        (quote.Subtotal.Amount, quote.Discount.Amount, quote.Total.Amount).Should().Be((100m, 10m, 90m));
    }

    [Fact]
    public async Task مشكلة_الكوبون_نتيجة_في_العرض_لا_استثناء()
    {
        Catalog(TestCatalog.Product(price: 20m, id: 1));
        _coupons.GetByCodeAsync("OLD", Arg.Any<CancellationToken>()).Returns(
            new Coupon("OLD", DiscountType.Percentage, 10, null, FixedClock.DefaultNow.UtcDateTime.AddDays(-1), null));
        _coupons.GetByCodeAsync("BIG", Arg.Any<CancellationToken>()).Returns(
            new Coupon("BIG", DiscountType.Percentage, 10, new Money(100, "JOD"), null, null));

        var missing = await Pricing().QuoteAsync([new(1, 1)], "NOPE", null, null, CancellationToken.None);
        var expired = await Pricing().QuoteAsync([new(1, 1)], "OLD", null, null, CancellationToken.None);
        var belowMinimum = await Pricing().QuoteAsync([new(1, 1)], "BIG", null, null, CancellationToken.None);
        var disabled = await Pricing(StoreWithoutModules()).QuoteAsync([new(1, 1)], "OLD", null, null, CancellationToken.None);

        new[] { missing, expired, belowMinimum, disabled }.Select(q => (q.Coupon!.Applied, q.Coupon.ErrorCode))
            .Should().Equal((false, "CouponNotFound"), (false, "InvalidCoupon"), (false, "InvalidCoupon"), (false, "ModuleDisabled"));
        new[] { missing, expired, belowMinimum, disabled }.Should().OnlyContain(q => q.Discount.Amount == 0 && q.Total.Amount == 20m);
    }

    [Fact]
    public async Task حدّ_العميل_يُحتسب_للعميل_المعروف_لا_للزائر()
    {
        Catalog(TestCatalog.Product(price: 20m, id: 1));
        var coupon = TestCatalog.WithId(new Coupon("ONCE", DiscountType.Percentage, 10, null, null, null, maxUsesPerCustomer: 1), 4);
        _coupons.GetByCodeAsync("ONCE", Arg.Any<CancellationToken>()).Returns(coupon);
        _redemptions.CountActiveAsync(4, 9, Arg.Any<CancellationToken>()).Returns(1);

        var usedUp = await Pricing().QuoteAsync([new(1, 1)], "ONCE", customerId: 9, shipping: null, CancellationToken.None);
        var guest = await Pricing().QuoteAsync([new(1, 1)], "ONCE", customerId: null, shipping: null, CancellationToken.None);

        (usedUp.Coupon!.Applied, usedUp.Coupon.ErrorCode).Should().Be((false, "InvalidCoupon"));
        guest.Coupon!.Applied.Should().BeTrue();
        await _redemptions.DidNotReceive().CountActiveAsync(Arg.Any<int>(), Arg.Is<int>(id => id != 9), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الطريقة_المختارة_تُسعَّر_بعد_الخصم_وتدخل_الإجمالي_وبلا_اختيار_يُطلب_اختيار()
    {
        Catalog(TestCatalog.Product(price: 50m, id: 1));
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>())
            .Returns(new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null));
        _shipping = TestShipping.Methods(TestShipping.Option(7, 3.5m), TestShipping.Option(8, 6m, name: "سريع"));

        var chosen = await Pricing().QuoteAsync([new(1, 2)], "SAVE10", null, new ShippingRequest(7, "JO"), CancellationToken.None);
        var unchosen = await Pricing().QuoteAsync([new(1, 2)], null, null, new ShippingRequest(null, "JO"), CancellationToken.None);
        var wrong = await Pricing().QuoteAsync([new(1, 2)], null, null, new ShippingRequest(99, "JO"), CancellationToken.None);

        (chosen.Shipping.Amount, chosen.Total.Amount, chosen.ShippingOutcome!.Selected!.MethodId, chosen.ShippingOutcome.ErrorCode)
            .Should().Be((3.5m, 93.5m, 7, (string?)null));
        (unchosen.Shipping.Amount, unchosen.Total.Amount, unchosen.ShippingOutcome!.ErrorCode, unchosen.ShippingOutcome.Options.Count)
            .Should().Be((0m, 100m, "ShippingMethodRequired", 2));
        (wrong.ShippingOutcome!.ErrorCode, wrong.Shipping.Amount).Should().Be(("ShippingMethodUnavailable", 0m));
        // حدّ المجانية يُقاس بالإجمالي بعد الخصم: المزوّد سُئل بـ 90 لا 100.
        await _shipping.Received().QuoteAsync(Arg.Is<Money>(m => m.Amount == 90), "JO", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task متجر_بلا_طرق_شحنه_مجاني_بلا_اختيار_ومتجر_لا_تخدم_طرقه_العنوان_يُعلَم()
    {
        Catalog(TestCatalog.Product(price: 20m, id: 1));

        var free = await Pricing().QuoteAsync([new(1, 1)], null, null, new ShippingRequest(null, "JO"), CancellationToken.None);
        (free.ShippingOutcome!.Required, free.ShippingOutcome.ErrorCode, free.Total.Amount).Should().Be((false, (string?)null, 20m));

        _shipping = Substitute.For<IShippingRateProvider>();
        _shipping.QuoteAsync(Arg.Any<Money>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ShippingQuote([], StoreShips: true));
        var nowhere = await Pricing().QuoteAsync([new(1, 1)], null, null, new ShippingRequest(null, "EG"), CancellationToken.None);

        (nowhere.ShippingOutcome!.ErrorCode, nowhere.ShippingOutcome.Required).Should().Be(("ShippingNotAvailable", true));
    }

    // متجر عطّل كل الوحدات الاختيارية (منها الكوبونات، D-11).
    private static ITenantContext StoreWithoutModules()
    {
        var context = new TenantContext();
        context.UseTenant(TestTenant.Info() with { Modules = new HashSet<string>() });
        return context;
    }

    // ── المتغيّرات (ProductVariants.md، V1) ── منتج بمتغيّرين: الافتراضي (معرّفه معرّف المنتج) ومتغيّر ثانٍ.
    private static Product Shirt(int id = 1, int secondVariantId = 12)
    {
        var shirt = TestCatalog.Product("قميص", price: 20m, id: id);
        shirt.SetPricing(new Money(20m, "JOD"), null, "SHIRT-S");
        TestCatalog.WithId(TestCatalog.AddVariant(shirt, new Money(25m, "JOD"), sku: "SHIRT-L"), secondVariantId);
        return shirt;
    }

    [Fact]
    public async Task كل_سطر_يُسعَّر_بمتغيّره_المسمّى_ويحمل_SKU_لقطةً()
    {
        Catalog(Shirt());

        var quote = await Pricing().QuoteAsync([new(1, 1, 1), new(1, 2, 12)], null, null, null, CancellationToken.None);

        quote.Lines.Select(l => (l.ProductId, l.VariantId, l.UnitPrice.Amount, l.LineTotal.Amount, l.Sellable, l.Sku, l.VariantLabel))
            .Should().Equal((1, 1, 20m, 20m, true, "SHIRT-S", "S"), (1, 12, 25m, 50m, true, "SHIRT-L", "L"));
        quote.Subtotal.Amount.Should().Be(70m);
    }

    [Fact]
    public async Task متغيّر_منتج_آخر_أو_معطّل_لا_يُسعَّر_ومنتج_بأكثر_من_متغيّر_بلا_تحديد_يطلبه()
    {
        var shirt = Shirt();
        shirt.DeactivateVariant(12);
        TestCatalog.WithId(TestCatalog.AddVariant(shirt, new Money(30m, "JOD")), 13);
        Catalog(shirt, TestCatalog.Product("شاحن", price: 4m, id: 2));

        var quote = await Pricing().QuoteAsync(
            [new(2, 1, 13), new(1, 1, 12), new(1, 1), new(2, 1)], null, null, null, CancellationToken.None);

        quote.Lines.Select(l => (l.ProductId, l.Sellable, l.VariantRequired))
            .Should().Equal((2, false, false), (1, false, false), (1, false, true), (2, true, false));
        quote.Lines[0].UnitPrice.Amount.Should().Be(0, "متغيّر القميص لا يسعّر الشاحن أبداً");
        (quote.Subtotal.Amount, quote.Total.Amount).Should().Be((4m, 4m));
    }

    [Fact]
    public async Task منتج_بسيط_بلا_متغيّر_مسمّى_يُسعَّر_بمتغيّره_الافتراضي_كما_كان()
    {
        Catalog(TestCatalog.Product("سماعات", price: 12.5m, id: 1));

        var quote = await Pricing().QuoteAsync([new(1, 2)], null, null, null, CancellationToken.None);

        quote.Lines.Single().Should().BeEquivalentTo(new { VariantId = 1, Sellable = true, VariantRequired = false });
        quote.Total.Amount.Should().Be(25m);
    }
}
