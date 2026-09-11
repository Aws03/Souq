using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

public class ReviewTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

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

    // ── الإشراف (المرحلة 13) ────────────────────────────────────────────────

    [Fact]
    public void الجديد_معلّق_غير_منشور_ما_لم_يعتمده_المتجر_تلقائياً()
    {
        var pending = new Review(1, 1, 1, 5, "جيد");
        pending.Status.Should().Be(ReviewStatus.Pending);
        pending.IsPublished.Should().BeFalse();

        var auto = new Review(1, 1, 1, 5, "جيد", approved: true);
        auto.Status.Should().Be(ReviewStatus.Approved);
        auto.IsPublished.Should().BeTrue();
        auto.ModeratedByUserId.Should().BeNull("الاعتماد التلقائي سياسة متجر لا قرار مشرف");
    }

    [Fact]
    public void الاعتماد_ينشر_ويسجّل_المشرف_والوقت()
    {
        var review = new Review(1, 1, 1, 4, "جيد");

        review.Approve(moderatorUserId: 9, Now);

        review.IsPublished.Should().BeTrue();
        review.ModeratedByUserId.Should().Be(9);
        review.ModeratedAt.Should().Be(Now);
        review.ModerationNote.Should().BeNull();
    }

    [Fact]
    public void الرفض_يخفي_المعتمد_ويحفظ_الملاحظة_مقصوصة_والاعتماد_يعيده_بلا_ملاحظة()
    {
        var review = new Review(1, 1, 1, 1, "سيئ", approved: true);

        review.Reject(9, Now, "  لغة مسيئة  ");
        review.Status.Should().Be(ReviewStatus.Rejected);
        review.IsPublished.Should().BeFalse();
        review.ModerationNote.Should().Be("لغة مسيئة");

        review.Approve(10, Now.AddHours(1));
        review.Status.Should().Be(ReviewStatus.Approved);
        review.ModeratedByUserId.Should().Be(10);
        review.ModerationNote.Should().BeNull();
    }

    [Fact]
    public void تكرار_القرار_نفسه_لا_يغيّر_من_قرّر_ولا_متى()
    {
        var review = new Review(1, 1, 1, 4, "جيد");
        review.Approve(9, Now);

        review.Approve(10, Now.AddDays(1));

        review.ModeratedByUserId.Should().Be(9);
        review.ModeratedAt.Should().Be(Now);
    }

    [Fact]
    public void قرار_بلا_مشرف_أو_بملاحظة_تتجاوز_500_حرف_يُرفض_ولا_يغيّر_الحالة()
    {
        var review = new Review(1, 1, 1, 4, "جيد");

        var noModerator = () => review.Approve(0, Now);
        var longNote = () => review.Reject(9, Now, new string('x', Review.ModerationNoteMaxLength + 1));

        noModerator.Should().Throw<InvalidReviewException>();
        longNote.Should().Throw<InvalidReviewException>();
        review.Status.Should().Be(ReviewStatus.Pending);
    }
}
