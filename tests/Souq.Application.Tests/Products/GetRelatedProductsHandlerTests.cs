using FluentAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Products;

public class GetRelatedProductsHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();

    private GetRelatedProductsHandler CreateHandler() => new(_products);

    private static Product NewProduct(int categoryId = 1) =>
        new("سماعات", "وصف", new Money(50), 10, "headphones", categoryId: categoryId);

    [Fact]
    public async Task منتج_غير_موجود_أو_معطّل_يُرجع_NotFound()
    {
        _products.GetActiveByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateHandler().Handle(new GetRelatedProductsQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task يستبعد_المنتج_الحالي_ويستخدم_فئته_وعدده_الافتراضي()
    {
        var product = NewProduct(categoryId: 3);
        typeof(Souq.Domain.Common.Entity).GetProperty("Id")!.SetValue(product, 1);
        _products.GetActiveByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _products.GetRelatedAsync(1, 3, 6, Arg.Any<CancellationToken>())
            .Returns(new List<Product> { NewProduct(categoryId: 3) });

        var result = await CreateHandler().Handle(new GetRelatedProductsQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        await _products.Received(1).GetRelatedAsync(1, 3, 6, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task يمرّر_العدد_المطلوب_للمستودع()
    {
        var product = NewProduct(categoryId: 2);
        typeof(Souq.Domain.Common.Entity).GetProperty("Id")!.SetValue(product, 5);
        _products.GetActiveByIdAsync(5, Arg.Any<CancellationToken>()).Returns(product);
        _products.GetRelatedAsync(5, 2, 3, Arg.Any<CancellationToken>())
            .Returns(new List<Product>());

        await CreateHandler().Handle(new GetRelatedProductsQuery(5, 3), CancellationToken.None);

        await _products.Received(1).GetRelatedAsync(5, 2, 3, Arg.Any<CancellationToken>());
    }
}
