using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// الانتقالات غير الصالحة يحرسها الكيان ويرفعها InvalidOrderOperationException (422 مركزياً، ADR-0017) — المعالج لا
// يلتقطها. الإلغاء (المرحلة 6) يسلّم المرجع لوحدة Inventory في معاملة الإلغاء: الحجز يُحرَّر والبيع المدفوع يعود.
public class UpdateOrderStatusHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInventoryReservations _reservations = Substitute.For<IInventoryReservations>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    private readonly Souq.Application.Features.Payments.Contracts.IOrderPayments _payments =
        Substitute.For<Souq.Application.Features.Payments.Contracts.IOrderPayments>();
    private readonly Souq.Application.Common.Interfaces.IPaymentService _gateway =
        Substitute.For<Souq.Application.Common.Interfaces.IPaymentService>();

    private UpdateOrderStatusHandler CreateHandler()
    {
        var coupons = Substitute.For<Souq.Application.Features.Coupons.Contracts.ICouponRedemptions>();
        return new(_orders, _reservations, coupons, _payments,
            new OrderPaymentConfirmation(_orders, _reservations, coupons, _payments,
                Substitute.For<Souq.Application.Features.Baskets.Contracts.IBasketCheckout>(), _gateway, _uow),
            TestCurrentUser.Admin(), _uow);
    }

    private static Order OrderInStatus(OrderStatus status)
    {
        var order = TestCatalog.WithId(new Order(customerId: 1, "عمّان", "JOD"), 4);
        order.AddItem(1, "سماعات", new Money(50, "JOD"), 2);
        if (status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered) order.MarkAsPaid();
        if (status is OrderStatus.Shipped or OrderStatus.Delivered) order.MarkAsShipped();
        if (status is OrderStatus.Delivered) order.MarkAsDelivered();
        if (status is OrderStatus.Cancelled) order.Cancel();
        return order;
    }

    private Task NoInventoryCall() =>
        _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task طلب_غير_موجود_يُرجع_NotFound()
    {
        _orders.GetWithItemsAsync(99, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await CreateHandler().Handle(new UpdateOrderStatusCommand(99, OrderStatusAction.Ship), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task Ship_من_Paid_ينجح_بلا_مساس_بالمخزون()
    {
        var order = OrderInStatus(OrderStatus.Paid);
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Ship), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Shipped);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await NoInventoryCall();
    }

    [Fact]
    public async Task Ship_من_Pending_يُرفض_بانتقال_غير_صالح_ولا_يحفظ()
    {
        var order = OrderInStatus(OrderStatus.Pending);
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Ship), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOrderOperationException>()).Which.Code.Should().Be("InvalidOrderOperation");
        order.Status.Should().Be(OrderStatus.Pending);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_من_Shipped_ينجح()
    {
        var order = OrderInStatus(OrderStatus.Shipped);
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Deliver), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public async Task Deliver_من_Paid_يُرفض_ولا_يحفظ()
    {
        var order = OrderInStatus(OrderStatus.Paid);
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Deliver), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOrderOperationException>();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    public async Task Cancel_من_Pending_أو_Paid_يسلّم_مرجع_الطلب_للمخزون_في_معاملة_الإلغاء(OrderStatus status)
    {
        // Phase 0 C2: كان الإلغاء لا يعيد المخزون المحجوز إطلاقاً. الآن: الحجز يُحرَّر (معلّق) أو يعود البيع (مدفوع).
        var order = OrderInStatus(status);
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(
            new UpdateOrderStatusCommand(4, OrderStatusAction.Cancel, Note: "طلب العميل"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        await _reservations.Received(1).CancelAsync(OrderStockReference.For(4), "طلب العميل", false, Arg.Any<CancellationToken>());
        await _uow.Received(1).InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task إلغاء_طلب_مدفوع_يردّ_ماله_كاملاً_وإلغاء_غير_المدفوع_لا_يستردّ()
    {
        var paid = OrderInStatus(OrderStatus.Paid);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(paid);

        (await CreateHandler().Handle(new UpdateOrderStatusCommand(1, OrderStatusAction.Cancel, "منتج تالف"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        // كل المتبقّي (بلا مبلغ)، بسبب الإلغاء، باسم الموظّف — بعد التزام معاملة الإلغاء.
        await _payments.Received(1).RefundAsync(paid.Id, null, "منتج تالف", Arg.Is<int?>(id => id != null), Arg.Any<CancellationToken>());

        _payments.ClearReceivedCalls();
        _orders.GetWithItemsAsync(2, Arg.Any<CancellationToken>()).Returns(OrderInStatus(OrderStatus.Pending));
        await CreateHandler().Handle(new UpdateOrderStatusCommand(2, OrderStatusAction.Cancel), CancellationToken.None);
        await _payments.DidNotReceiveWithAnyArgs().RefundAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task إلغاء_الإدارة_لطلب_لم_يُدفع_يسأل_البوّابة_أولاً_فلا_يُلغى_طلب_دُفع_للتوّ()
    {
        var justPaid = OrderInStatus(OrderStatus.Pending);
        justPaid.SetPaymentIntent("pi_race");
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(justPaid);
        _gateway.CancelIntentAsync("pi_race", Arg.Any<CancellationToken>())
            .Returns(Souq.Application.Common.Interfaces.PaymentIntentState.Succeeded);
        _gateway.ConfirmAsync("pi_race", Arg.Any<CancellationToken>())
            .Returns(new Souq.Application.Common.Interfaces.PaymentConfirmationResult(true, null));

        var result = await CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Cancel), CancellationToken.None);

        result.ErrorCode.Should().Be("OrderAlreadyPaid");
        justPaid.Status.Should().Be(OrderStatus.Paid, "دفعه العميل قبل الإلغاء: يُؤكَّد ولا يُلغى");
        await _payments.Received(1).MarkSucceededAsync(4, Arg.Any<CancellationToken>());
        await NoInventoryCall();
    }

    [Fact]
    public async Task إلغاء_الإدارة_لطلب_لم_يُدفع_يُلغي_نيّته_ويحسم_دفعته_ملغاة()
    {
        var pending = OrderInStatus(OrderStatus.Pending);
        pending.SetPaymentIntent("pi_open");
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(pending);
        _gateway.CancelIntentAsync("pi_open", Arg.Any<CancellationToken>())
            .Returns(Souq.Application.Common.Interfaces.PaymentIntentState.Cancelled);

        (await CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Cancel), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        pending.Status.Should().Be(OrderStatus.Cancelled);
        await _payments.Received(1).MarkClosedAsync(4, false, Arg.Any<CancellationToken>());
        await _payments.DidNotReceiveWithAnyArgs().RefundAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Cancel_بلا_ملاحظة_يوثّق_أنه_من_الإدارة()
    {
        var order = OrderInStatus(OrderStatus.Pending);
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(order);

        await CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Cancel), CancellationToken.None);

        await _reservations.Received(1).CancelAsync(OrderStockReference.For(4), "إلغاء من الإدارة", false, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Cancel_من_Shipped_أو_لطلب_ملغى_يُرفض_ولا_يمسّ_المخزون(OrderStatus status)
    {
        var order = OrderInStatus(status);
        _orders.GetWithItemsAsync(4, Arg.Any<CancellationToken>()).Returns(order);

        var act = () => CreateHandler().Handle(new UpdateOrderStatusCommand(4, OrderStatusAction.Cancel), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOrderOperationException>();
        await NoInventoryCall();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
