using FluentAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

public class ReviewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void تقييم_خارج_1_5_يُرفض(int rating)
    {
        var act = () => new Review(productId: 1, customerId: 1, orderId: 1, rating, "تعليق جيد");
        act.Should().Throw<InvalidReviewException>();
    }

    [Fact]
    public void تعليق_فارغ_يُرفض()
    {
        var act = () => new Review(1, 1, 1, rating: 5, comment: "   ");
        act.Should().Throw<InvalidReviewException>();
    }

    [Fact]
    public void تعليق_أطول_من_1000_حرف_يُرفض()
    {
        var longComment = new string('ا', 1001);
        var act = () => new Review(1, 1, 1, 5, longComment);
        act.Should().Throw<InvalidReviewException>();
    }

    [Fact]
    public void إنشاء_صالح_يخزّن_البيانات_ويُقلّم_التعليق()
    {
        var review = new Review(productId: 3, customerId: 7, orderId: 42, rating: 4, comment: "  منتج ممتاز  ");

        review.ProductId.Should().Be(3);
        review.CustomerId.Should().Be(7);
        review.OrderId.Should().Be(42);
        review.Rating.Should().Be(4);
        review.Comment.Should().Be("منتج ممتاز");
    }
}
