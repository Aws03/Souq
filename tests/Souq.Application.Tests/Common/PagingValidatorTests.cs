using AwesomeAssertions;
using Souq.Application.Features.Coupons.Queries;
using Souq.Application.Features.Inventory.Queries;
using Souq.Application.Features.Orders.Queries;
using Souq.Application.Features.Products.Queries;
using Souq.Application.Features.Reviews.Queries;

namespace Souq.Application.Tests.Common;

// Phase 0 C9: page=0 كان يُنتج OFFSET سالباً ⇒ خطأ SQL ⇒ 500، وpageSize بلا سقف.
public class PagingValidatorTests
{
    private readonly GetProductsQueryValidator _products = new();

    [Fact]
    public void الافتراضيات_صالحة()
    {
        _products.Validate(new GetProductsQuery()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(-1, 12)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(1, 1_000_000)]
    public void صفحة_أو_حجم_خارج_الحدود_يُرفض(int page, int pageSize)
    {
        _products.Validate(new GetProductsQuery(Page: page, PageSize: pageSize)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void الحد_الأعلى_للحجم_مقبول()
    {
        _products.Validate(new GetProductsQuery(PageSize: 100)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void نطاق_سعر_مقلوب_أو_سالب_يُرفض()
    {
        _products.Validate(new GetProductsQuery(MinPrice: 50, MaxPrice: 10)).IsValid.Should().BeFalse();
        _products.Validate(new GetProductsQuery(MinPrice: -1)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void معرّفات_فئات_غير_صالحة_تُرفض()
    {
        _products.Validate(new GetProductsQuery(CategoryIds: new List<int> { 1, 0 })).IsValid.Should().BeFalse();
    }

    [Fact]
    public void قوائم_الإدارة_والتقييمات_محروسة_بنفس_الحدود()
    {
        new GetOrdersQueryValidator().Validate(new GetOrdersQuery(Page: 0)).IsValid.Should().BeFalse();
        new GetCouponsQueryValidator().Validate(new GetCouponsQuery(PageSize: 1000)).IsValid.Should().BeFalse();
        new GetProductReviewsQueryValidator().Validate(new GetProductReviewsQuery(ProductId: 0)).IsValid.Should().BeFalse();
        new GetRelatedProductsQueryValidator().Validate(new GetRelatedProductsQuery(1, Count: 500)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void القوائم_التي_كانت_بلا_حدّ_صارت_مرقّمة_ومحروسة()
    {
        // "طلباتي" والجرد والمنخفض وسجلّ الحركة كانت تعيد كل الصفوف (Part 15).
        new GetMyOrdersQueryValidator().Validate(new GetMyOrdersQuery(PageSize: 101)).IsValid.Should().BeFalse();
        new GetInventoryQueryValidator().Validate(new GetInventoryQuery(Page: 0)).IsValid.Should().BeFalse();
        new GetLowStockQueryValidator().Validate(new GetLowStockQuery(PageSize: 0)).IsValid.Should().BeFalse();
        new GetStockMovementsQueryValidator().Validate(new GetStockMovementsQuery(ProductId: 0)).IsValid.Should().BeFalse();

        new GetMyOrdersQueryValidator().Validate(new GetMyOrdersQuery()).IsValid.Should().BeTrue();
        new GetInventoryQueryValidator().Validate(new GetInventoryQuery()).IsValid.Should().BeTrue();
        new GetStockMovementsQueryValidator().Validate(new GetStockMovementsQuery(ProductId: 3, PageSize: 100)).IsValid.Should().BeTrue();
    }
}
