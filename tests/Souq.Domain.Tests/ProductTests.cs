using FluentAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

public class ProductTests
{
    private static Product NewProduct(int stock = 10) =>
        new("سماعات لاسلكية", "صوت نقي", new Money(59.9m), stock, "headphones", categoryId: 1);

    [Fact]
    public void جديد_يكون_نشطاً_دائماً()
    {
        NewProduct().IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(5, 3, true)]
    [InlineData(5, 5, true)]
    [InlineData(5, 6, false)]
    [InlineData(5, 0, false)]
    public void CanFulfill_يعتمد_على_المخزون_والكمية(int stock, int requested, bool expected)
    {
        var product = NewProduct(stock);

        product.CanFulfill(requested).Should().Be(expected);
    }

    [Fact]
    public void CanFulfill_يرفض_دائماً_لمنتج_معطّل()
    {
        var product = NewProduct(10);
        product.Deactivate();

        product.CanFulfill(1).Should().BeFalse();
    }

    [Fact]
    public void DecreaseStock_ينقص_المخزون_عند_توفّره()
    {
        var product = NewProduct(10);

        product.DecreaseStock(4);

        product.StockQuantity.Should().Be(6);
    }

    [Fact]
    public void DecreaseStock_يرمي_عند_عدم_توفّر_الكمية()
    {
        var product = NewProduct(2);

        var act = () => product.DecreaseStock(3);

        act.Should().Throw<InsufficientStockException>();
        product.StockQuantity.Should().Be(2); // لا تغيير جزئي عند الفشل
    }

    [Fact]
    public void DecreaseStock_لمنتج_معطّل_يرمي_حتى_مع_توفّر_المخزون()
    {
        var product = NewProduct(10);
        product.Deactivate();

        var act = () => product.DecreaseStock(1);

        act.Should().Throw<InsufficientStockException>();
    }

    [Fact]
    public void IncreaseStock_يزيد_المخزون()
    {
        var product = NewProduct(5);

        product.IncreaseStock(3);

        product.StockQuantity.Should().Be(8);
    }

    [Fact]
    public void SetStock_يرفض_القيمة_السالبة()
    {
        var product = NewProduct();

        var act = () => product.SetStock(-1);

        act.Should().Throw<InvalidProductDataException>();
    }

    [Fact]
    public void SetStock_يقبل_الصفر_ويعيّن_القيمة_المطلقة()
    {
        var product = NewProduct(10);

        product.SetStock(0);

        product.StockQuantity.Should().Be(0);
    }

    [Fact]
    public void SetImageUrl_يرفض_رابطاً_فارغاً()
    {
        var product = NewProduct();

        var act = () => product.SetImageUrl("   ");

        act.Should().Throw<InvalidProductDataException>();
    }

    [Fact]
    public void SetImageUrl_يعيّن_الرابط_الصالح()
    {
        var product = NewProduct();

        product.SetImageUrl("/uploads/abc.jpg");

        product.ImageUrl.Should().Be("/uploads/abc.jpg");
    }

    [Fact]
    public void Deactivate_ثم_Activate_يعيد_المنتج_نشطاً()
    {
        var product = NewProduct();

        product.Deactivate();
        product.IsActive.Should().BeFalse();

        product.Activate();
        product.IsActive.Should().BeTrue();
    }
}
