using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Baskets;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Baskets;

// الدفع من السلة (المرحلة 9): الطلب يقرأ أسطر سلة العميل، والدفع يستهلك منها ما اشتُري فقط — ما أُضيف بعده يبقى.
public class BasketCheckoutTests
{
    private readonly IBasketRepository _baskets = Substitute.For<IBasketRepository>();
    private readonly FixedClock _clock = new();

    private BasketCheckout Checkout() => new(_baskets, new BasketSettings(), _clock);

    private Basket CustomerBasket(params (int Product, int Quantity)[] lines) =>
        CustomerBasket(lines.Select(l => (l.Product, l.Product, l.Quantity)).ToArray());

    private Basket CustomerBasket(params (int Product, int Variant, int Quantity)[] lines)
    {
        var basket = Basket.ForCustomer(1, _clock.UtcNow.AddDays(1));
        foreach (var (product, variant, quantity) in lines) basket.Add(product, variant, quantity, _clock.UtcNow.AddDays(1));
        _baskets.GetForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(basket);
        return basket;
    }

    [Fact]
    public async Task أسطر_السلة_للطلب_والمنتهية_كأنها_فارغة()
    {
        CustomerBasket((5, 2), (6, 1));

        (await Checkout().LinesForCustomerAsync(1, CancellationToken.None))
            .Should().Equal(new PricingLine(5, 2, 5), new PricingLine(6, 1, 6));

        _baskets.GetForCustomerAsync(1, Arg.Any<CancellationToken>())
            .Returns(Basket.ForCustomer(1, _clock.UtcNow.AddMinutes(-1)));
        (await Checkout().LinesForCustomerAsync(1, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task الدفع_يستهلك_المشترى_فقط()
    {
        var basket = CustomerBasket((5, 3), (6, 1), (7, 2));

        await Checkout().ConsumeAsync(1, [new PricingLine(5, 2, 5), new PricingLine(6, 1, 6), new PricingLine(8, 4, 8)], CancellationToken.None);

        basket.Lines.Select(l => (l.ProductId, l.Quantity)).Should().BeEquivalentTo(new[] { (5, 1), (7, 2) });
    }

    [Fact]
    public async Task متغيّرا_المنتج_نفسه_سطران_في_الطلب_والدفع_يستهلك_المشترى_منهما_وحده()
    {
        var basket = CustomerBasket((5, 51, 2), (5, 52, 3));

        (await Checkout().LinesForCustomerAsync(1, CancellationToken.None))
            .Should().Equal(new PricingLine(5, 2, 51), new PricingLine(5, 3, 52));

        await Checkout().ConsumeAsync(1, [new PricingLine(5, 1, 52)], CancellationToken.None);

        basket.Lines.Select(l => (l.VariantId, l.Quantity)).Should().BeEquivalentTo(new[] { (51, 2), (52, 2) });
    }
}
