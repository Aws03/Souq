using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Baskets.Pricing;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Baskets;

// خطّ التسعير الواحد (المرحلة 8): أسعار الكتالوج الحيّة، الأسطر غير القابلة للبيع خارج المجموع، الشحن والضريبة صفر صريح،
// والكوبون نتيجة في العرض لا استثناء — والدفع يستعمل الخطّ نفسه (CreateOrderHandlerTests).
public class PricingServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();

    private PricingService Pricing(ITenantContext? store = null) =>
        new(_products, _coupons, store ?? TestTenant.Context(), new FixedClock());

    private void Catalog(params Product[] products) =>
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(products.ToList());

    [Fact]
    public async Task الأسطر_بأسعار_الكتالوج_والشحن_والضريبة_صفر_صريح()
    {
        Catalog(TestCatalog.Product("سماعات", price: 12.5m, id: 1), TestCatalog.Product("شاحن", price: 3m, id: 2));

        var quote = await Pricing().QuoteAsync([new(1, 2), new(2, 1)], null, CancellationToken.None);

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

        var quote = await Pricing().QuoteAsync([new(1, 1), new(2, 2), new(99, 1)], null, CancellationToken.None);

        quote.Lines.Select(l => (l.ProductId, l.Sellable)).Should().Equal((1, false), (2, true), (99, false));
        (quote.Subtotal.Amount, quote.Total.Amount).Should().Be((8m, 8m));
    }

    [Fact]
    public async Task الكوبون_المقبول_يخصم_من_الفرعي()
    {
        Catalog(TestCatalog.Product(price: 50m, id: 1));
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>())
            .Returns(new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null));

        var quote = await Pricing().QuoteAsync([new(1, 2)], " SAVE10 ", CancellationToken.None);

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

        var missing = await Pricing().QuoteAsync([new(1, 1)], "NOPE", CancellationToken.None);
        var expired = await Pricing().QuoteAsync([new(1, 1)], "OLD", CancellationToken.None);
        var belowMinimum = await Pricing().QuoteAsync([new(1, 1)], "BIG", CancellationToken.None);
        var disabled = await Pricing(StoreWithoutModules()).QuoteAsync([new(1, 1)], "OLD", CancellationToken.None);

        new[] { missing, expired, belowMinimum, disabled }.Select(q => (q.Coupon!.Applied, q.Coupon.ErrorCode))
            .Should().Equal((false, "CouponNotFound"), (false, "InvalidCoupon"), (false, "InvalidCoupon"), (false, "ModuleDisabled"));
        new[] { missing, expired, belowMinimum, disabled }.Should().OnlyContain(q => q.Discount.Amount == 0 && q.Total.Amount == 20m);
        await _coupons.DidNotReceive().GetByCodeAsync("OLD", Arg.Is<CancellationToken>(_ => false));
    }

    // متجر عطّل كل الوحدات الاختيارية (منها الكوبونات، D-11).
    private static ITenantContext StoreWithoutModules()
    {
        var context = new TenantContext();
        context.UseTenant(TestTenant.Info() with { Modules = new HashSet<string>() });
        return context;
    }
}
