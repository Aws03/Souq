using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// الانتقالات غير الصالحة يحرسها الكيان ويرفعها InvalidOrderOperationException (422
// مركزياً، ADR-0017) — المعالج لا يلتقطها. المهمّ هنا: لا مخزون يُمسّ ولا شيء يُحفَظ.
public class UpdateOrderStatusHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IStockMovementRepository _movements = Substitute.For<IStockMovementRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateOrderStatusHandler CreateHandler() =>
        new(_orders, new OrderStockRelease(_products, _movements), _uow);

    private static Order OrderInStatus(OrderStatus status, int quantity = 1)
    {
        var order = new Order(customerId: 1, "عمّان", "JOD");
        order.AddItem(1, "سماعات", new Money(50, "JOD"), quantity);
        if (status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered) order.MarkAsPaid();
        if (status is OrderStatus.Shipped or OrderStatus.Delivered) order.MarkAsShipped();
        if (status is OrderStatus.Delivered) order.MarkAsDelivered();
        if (status is OrderStatus.Cancelled) order.Cancel();
        return order;
    }

    private static Product ProductWithStock(int stock) =>
        new("سماعات", "وصف", new Money(50, "JOD"), stock, "headphones", categoryId: 1);

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
    public async Task Ship_من_Pending_يُرفض_بانتقال_غير_صالح_ولا_يحفظ()
    {
        var order = OrderInStatus(OrderStatus.Pending);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => CreateHandler().Handle(new UpdateOrderStatusCommand(1, OrderStatusAction.Ship), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOrderOperationException>()).Which.Code.Should().Be("InvalidOrderOperation");
        order.Status.Should().Be(OrderStatus.Pending);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
    public async Task Deliver_من_Paid_يُرفض_ولا_يحفظ()
    {
        var order = OrderInStatus(OrderStatus.Paid);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => CreateHandler().Handle(new UpdateOrderStatusCommand(1, OrderStatusAction.Deliver), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOrderOperationException>();
        order.Status.Should().Be(OrderStatus.Paid);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    public async Task Cancel_من_Pending_أو_Paid_يعيد_المخزون_المحجوز_ويسجّل_حركة_Cancellation(OrderStatus status)
    {
        // Phase 0 C2: كان الإلغاء لا يعيد المخزون المحجوز إطلاقاً — خسارة دائمة لكل طلب ملغى.
        var order = OrderInStatus(status, quantity: 2);
        var product = ProductWithStock(8);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(1, OrderStatusAction.Cancel, Note: "طلب العميل"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        product.StockQuantity.Should().Be(10);
        await _movements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Cancellation && m.QuantityChange == 2
                                       && m.NewQuantity == 10 && m.Note == "طلب العميل"),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_من_Shipped_يُرفض_ولا_يمسّ_المخزون()
    {
        var order = OrderInStatus(OrderStatus.Shipped);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => CreateHandler().Handle(new UpdateOrderStatusCommand(1, OrderStatusAction.Cancel), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOrderOperationException>();
        await _products.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _movements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_لطلب_ملغى_مسبقاً_يُرفض_ولا_يعيد_المخزون_مرتين()
    {
        var order = OrderInStatus(OrderStatus.Cancelled);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => CreateHandler().Handle(new UpdateOrderStatusCommand(1, OrderStatusAction.Cancel), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOrderOperationException>();
        await _movements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
