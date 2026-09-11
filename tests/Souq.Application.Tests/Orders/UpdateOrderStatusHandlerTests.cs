using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

public class UpdateOrderStatusHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateOrderStatusHandler CreateHandler() => new(_orders, _uow);

    private static Order OrderInStatus(OrderStatus status)
    {
        var order = new Order(customerId: 1, "عمّان");
        order.AddItem(1, "سماعات", new Money(50), 1);
        if (status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered) order.MarkAsPaid();
        if (status is OrderStatus.Shipped or OrderStatus.Delivered) order.MarkAsShipped();
        if (status is OrderStatus.Delivered) order.MarkAsDelivered();
        if (status is OrderStatus.Cancelled) order.Cancel();
        return order;
    }

    [Fact]
    public async Task طلب_غير_موجود_يُرجع_NotFound()
    {
        _orders.GetWithItemsAsync(99, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(99, OrderStatusAction.Ship), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task Ship_من_Paid_ينجح()
    {
        var order = OrderInStatus(OrderStatus.Paid);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(1, OrderStatusAction.Ship), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Shipped);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ship_من_Pending_يُرفض_بانتقال_غير_صالح()
    {
        var order = OrderInStatus(OrderStatus.Pending);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(1, OrderStatusAction.Ship), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidTransition");
    }

    [Fact]
    public async Task Deliver_من_Shipped_ينجح()
    {
        var order = OrderInStatus(OrderStatus.Shipped);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(1, OrderStatusAction.Deliver), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task Deliver_من_Paid_يُرفض()
    {
        var order = OrderInStatus(OrderStatus.Paid);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(1, OrderStatusAction.Deliver), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidTransition");
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    public async Task Cancel_من_Pending_أو_Paid_ينجح(OrderStatus status)
    {
        var order = OrderInStatus(status);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(1, OrderStatusAction.Cancel), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_من_Shipped_يُرفض()
    {
        var order = OrderInStatus(OrderStatus.Shipped);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(1, OrderStatusAction.Cancel), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidTransition");
    }
}
