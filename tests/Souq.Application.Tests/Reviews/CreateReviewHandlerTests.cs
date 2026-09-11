using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Reviews.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Reviews;

// تنسيق حالة الاستخدام: المقيِّم هو المستخدم الحالي، لا تقييم مكرّر، ولا تقييم بلا طلب مُسلَّم.
// "مُسلَّم يحوي المنتج" نفسه استعلام SQL (FindDeliveredOrderIdContainingAsync) يُثبَت على SQL
// Server في اختبارات التكامل (طلب مدفوع غير مُسلَّم ⇒ لا أحقّية).
public class CreateReviewHandlerTests
{
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateReviewHandler CreateHandler(TestCurrentUser? user = null) =>
        new(_reviews, _orders, user ?? TestCurrentUser.Customer(1), _uow);

    [Fact]
    public async Task عميل_بلا_طلب_مُسلَّم_يحوي_المنتج_يُرفض()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);
        _orders.FindDeliveredOrderIdContainingAsync(1, 5, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await CreateHandler().Handle(
            new CreateReviewCommand(ProductId: 5, Rating: 5, Comment: "ممتاز"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotEligible");
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task عميل_قيّم_المنتج_مسبقاً_يُرفض_بلا_فحص_الطلبات()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new CreateReviewCommand(5, 5, "ممتاز"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("AlreadyReviewed");
        await _orders.DidNotReceive().FindDeliveredOrderIdContainingAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task طلب_مُسلَّم_يحوي_المنتج_يُتيح_التقييم_باسم_المستخدم_الحالي_ويربطه_بالطلب()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);
        _orders.FindDeliveredOrderIdContainingAsync(1, 5, Arg.Any<CancellationToken>()).Returns(42);

        var result = await CreateHandler().Handle(new CreateReviewCommand(5, 5, "منتج رائع"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _reviews.Received(1).AddAsync(
            Arg.Is<Review>(r => r.ProductId == 5 && r.CustomerId == 1 && r.OrderId == 42 && r.Rating == 5),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بلا_مستخدم_مُصادَق_لا_يُكتب_تقييم()
    {
        var act = () => CreateHandler(TestCurrentUser.Anonymous()).Handle(new CreateReviewCommand(5, 5, "x"), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationRequiredException>();
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }
}
