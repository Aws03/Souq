using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

// السلة (المرحلة 8): الكميات وحدودها، الدمج من سلة زائر، المالك، والانتهاء المنزلق.
public class BasketTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddDays(30);
    private static readonly string GuestHash = new('a', Basket.GuestTokenHashLength);

    [Fact]
    public void الإضافة_تدمج_سطر_المتغيّر_وتمدّ_العمر()
    {
        var basket = Basket.ForGuest(GuestHash, Now);

        basket.Add(productId: 1, variantId: 11, quantity: 2, Later);
        basket.Add(1, 11, 3, Later.AddDays(1));

        basket.Lines.Should().ContainSingle().Which.Quantity.Should().Be(5);
        basket.ExpiresAt.Should().Be(Later.AddDays(1));
        basket.IsGuest.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Basket.MaxQuantityPerLine + 1)]
    public void كمية_خارج_حدّها_تُرفض(int quantity)
    {
        var basket = Basket.ForCustomer(7, Now);

        ((Action)(() => basket.Add(1, 11, quantity, Later))).Should().Throw<InvalidBasketOperationException>();
        basket.Lines.Should().BeEmpty();
    }

    [Fact]
    public void تجاوز_حدّ_السطر_بالإضافة_يُرفض_ولا_يُقصّ()
    {
        var basket = Basket.ForCustomer(7, Now);
        basket.Add(1, 11, Basket.MaxQuantityPerLine - 1, Later);

        ((Action)(() => basket.Add(1, 11, 2, Later))).Should().Throw<InvalidBasketOperationException>();
        basket.Lines.Single().Quantity.Should().Be(Basket.MaxQuantityPerLine - 1);
    }

    [Fact]
    public void السلة_محدودة_بعدد_الأصناف()
    {
        var basket = Basket.ForCustomer(7, Now);
        for (var i = 1; i <= Basket.MaxLines; i++) basket.Add(i, 100 + i, 1, Later);

        ((Action)(() => basket.Add(999, 999, 1, Later))).Should().Throw<InvalidBasketOperationException>();
        basket.Lines.Should().HaveCount(Basket.MaxLines);
    }

    [Fact]
    public void الكمية_صفر_تحذف_والسطر_الغائب_يُرفض()
    {
        var basket = Basket.ForCustomer(7, Now);
        basket.Add(1, 11, 2, Now);
        basket.Add(2, 22, 1, Now);

        basket.SetQuantity(11, 4, Later);
        basket.SetQuantity(22, 0, Later);

        basket.Lines.Should().ContainSingle().Which.Quantity.Should().Be(4);
        basket.LineFor(1)!.VariantId.Should().Be(11);
        basket.LineFor(2).Should().BeNull();
        ((Action)(() => basket.SetQuantity(22, 1, Later))).Should().Throw<InvalidBasketOperationException>();
        ((Action)(() => basket.Remove(22, Later))).Should().Throw<InvalidBasketOperationException>();
    }

    [Fact]
    public void الدمج_يجمع_الكميات_مقصوصةً_ويضيف_الجديد()
    {
        var customer = Basket.ForCustomer(7, Now);
        customer.Add(1, 11, 98, Now);
        var guest = Basket.ForGuest(GuestHash, Now);
        guest.Add(1, 11, 5, Now);
        guest.Add(2, 22, 1, Now);

        customer.MergeFrom(guest, Later);

        customer.Lines.Select(l => (l.VariantId, l.Quantity))
            .Should().BeEquivalentTo(new[] { (11, Basket.MaxQuantityPerLine), (22, 1) });
        customer.ExpiresAt.Should().Be(Later);
    }

    [Fact]
    public void الدمج_من_سلة_زائر_إلى_سلة_عميل_فقط()
    {
        var guest = Basket.ForGuest(GuestHash, Now);

        ((Action)(() => guest.MergeFrom(Basket.ForGuest(GuestHash, Now), Later))).Should().Throw<InvalidBasketOperationException>();
        ((Action)(() => Basket.ForCustomer(7, Now).MergeFrom(Basket.ForCustomer(8, Now), Later)))
            .Should().Throw<InvalidBasketOperationException>();
    }

    [Fact]
    public void المالك_صالح_والانتهاء_بالتاريخ()
    {
        ((Action)(() => Basket.ForGuest("short", Now))).Should().Throw<InvalidBasketOperationException>();
        ((Action)(() => Basket.ForCustomer(0, Now))).Should().Throw<InvalidBasketOperationException>();

        var basket = Basket.ForGuest(GuestHash, Later);
        basket.IsExpired(Now).Should().BeFalse();
        basket.IsExpired(Later).Should().BeTrue();
    }

    [Fact]
    public void التفريغ_يحذف_الأسطر_ويمدّ_العمر()
    {
        var basket = Basket.ForCustomer(7, Now);
        basket.Add(1, 11, 1, Now);

        basket.Clear(Later);

        basket.Lines.Should().BeEmpty();
        basket.ExpiresAt.Should().Be(Later);
    }
}
