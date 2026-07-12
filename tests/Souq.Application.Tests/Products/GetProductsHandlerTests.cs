using FluentAssertions;
using NSubstitute;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Products;

public class GetProductsHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();

    private static Product NewProduct() =>
        new("سماعات", "وصف", new Money(50), 10, "headphones", categoryId: 1);

    [Fact]
    public async Task يمرّر_كل_الفلاتر_والترتيب_للمستودع_كما_هي()
    {
        var cats = new List<int> { 1, 2 };
        _products.SearchAsync("سما", cats, 2, 12, 10m, 100m, ProductSortBy.PriceAsc, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Product>)new List<Product> { NewProduct() }, 25));

        var handler = new GetProductsHandler(_products);
        var result = await handler.Handle(
            new GetProductsQuery("سما", cats, Page: 2, PageSize: 12,
                MinPrice: 10m, MaxPrice: 100m, SortBy: ProductSortBy.PriceAsc),
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(25);
        result.PageNumber.Should().Be(2);
        await _products.Received(1).SearchAsync(
            "سما", cats, 2, 12, 10m, 100m, ProductSortBy.PriceAsc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بلا_فلاتر_يستخدم_الافتراضيات_والترتيب_الأحدث()
    {
        _products.SearchAsync(null, null, 1, 12, null, null, ProductSortBy.Newest, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Product>)new List<Product>(), 0));

        var handler = new GetProductsHandler(_products);
        var result = await handler.Handle(new GetProductsQuery(), CancellationToken.None);

        result.Items.Should().BeEmpty();
        await _products.Received(1).SearchAsync(
            null, null, 1, 12, null, null, ProductSortBy.Newest, Arg.Any<CancellationToken>());
    }
}
