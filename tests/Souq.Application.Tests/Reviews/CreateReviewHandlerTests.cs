using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Reviews.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Reviews;

// يغطّي القاعدة الأهم في هذه الميزة: لا تقييم بلا طلب مُسلَّم فعلياً يحوي المنتج.
public class CreateReviewHandlerTests
{
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateReviewHandler CreateHandler() => new(_reviews, _orders, _uow);

    [Fact]
    public async Task عميل_بلا_طلب_مُسلَّم_يحوي_المنتج_يُرفض()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);
        _orders.GetByCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(new List<Order>());

        var result = await CreateHandler().Handle(
            new CreateReviewCommand(ProductId: 5, CustomerId: 1, Rating: 5, Comment: "ممتاز"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotEligible");
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task طلب_مدفوع_لكن_غير_مُسلَّم_بعد_يُرفض()
    {
        var order = new Order(1, "عمّان");
        order.AddItem(5, "سماعات", new Money(50), 1);
        order.MarkAsPaid(); // لم يُشحن ولم يُسلَّم بعد

        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);
        _orders.GetByCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(new List<Order> { order });

        var result = await CreateHandler().Handle(
            new CreateReviewCommand(5, 1, 4, "جيد"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotEligible");
    }

    [Fact]
    public async Task عميل_قيّم_المنتج_مسبقاً_يُرفض_بلا_فحص_الطلبات()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new CreateReviewCommand(5, 1, 5, "ممتاز"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("AlreadyReviewed");
        await _orders.DidNotReceive().GetByCustomerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task طلب_مُسلَّم_يحوي_المنتج_يُتيح_التقييم()
    {
        var order = new Order(1, "عمّان");
        order.AddItem(5, "سماعات", new Money(50), 1);
        order.MarkAsPaid();
        order.MarkAsShipped();
        order.MarkAsDelivered();

        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);
        _orders.GetByCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(new List<Order> { order });

        var result = await CreateHandler().Handle(
            new CreateReviewCommand(5, 1, 5, "منتج رائع"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _reviews.Received(1).AddAsync(
            Arg.Is<Review>(r => r.ProductId == 5 && r.CustomerId == 1 && r.OrderId == order.Id && r.Rating == 5),
            Arg.Any<CancellationToken>());
    }
}
