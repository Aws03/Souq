using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

using Souq.Application.Tests.Analytics;

namespace Souq.Application.Tests.Orders;

// إلغاء العميل طلبه (المرحلة 9): صاحبه فقط، قبل الدفع فقط، والبوّابة تُسأل أولاً عن نيّة الدفع — لا يُلغى طلب قد يكون دُفع.
public class CancelMyOrderHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInventoryReservations _reservations = Substitute.For<IInventoryReservations>();
    private readonly IPaymentService _payment = PaymentServiceFake.Create();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    private CancelMyOrderHandler Handler(int customerId = 1) => new(
        _orders,
        new OrderPaymentConfirmation(_orders, _reservations,
            Substitute.For<Souq.Application.Features.Coupons.Contracts.ICouponRedemptions>(),
            Substitute.For<Souq.Application.Features.Payments.Contracts.IOrderPayments>(),
            Substitute.For<IBasketCheckout>(), _payment, _uow, NullLogger<OrderPaymentConfirmation>.Instance, new RecordingEventSink()),
        TestCurrentUser.Customer(customerId));

    private Order Arrange(string? intent = null, bool paid = false)
    {
        var order = TestCatalog.WithId(new Order(customerId: 1, "عمّان", "JOD"), 9);
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        if (intent is not null) order.SetPaymentIntent(intent);
        if (paid) order.MarkAsPaid();
        _orders.GetWithItemsAsync(9, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task صاحب_الطلب_يلغيه_قبل_الدفع_ويُحرَّر_حجزه_ويُسجَّل_أنه_العميل()
    {
        var order = Arrange();

        var result = await Handler().Handle(new CancelMyOrderCommand(9, "غيّرت رأيي"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        (order.StatusHistory.Last().ChangedBy, order.StatusHistory.Last().Note).Should().Be((OrderActorKind.Customer, "غيّرت رأيي"));
        await _reservations.Received(1).CancelAsync(OrderStockReference.For(9), "غيّرت رأيي", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task عميل_آخر_404_والطلب_المدفوع_يُرفض_بلا_أثر()
    {
        var order = Arrange(paid: true);

        (await Handler(customerId: 2).Handle(new CancelMyOrderCommand(9), CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        (await Handler().Handle(new CancelMyOrderCommand(9), CancellationToken.None)).ErrorCode.Should().Be("InvalidOrderOperation");

        order.Status.Should().Be(OrderStatus.Paid);
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task دفع_قيد_المعالجة_يمنع_الإلغاء_الآن()
    {
        var order = Arrange(intent: "pi_1");
        _payment.CancelIntentAsync("pi_1", Arg.Any<CancellationToken>()).Returns(PaymentIntentState.Processing);

        (await Handler().Handle(new CancelMyOrderCommand(9), CancellationToken.None)).ErrorCode.Should().Be("PaymentProcessing");

        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task دفع_نجح_قبل_الإلغاء_يُؤكَّد_ويُرفض_الإلغاء()
    {
        var order = Arrange(intent: "pi_1");
        _payment.CancelIntentAsync("pi_1", Arg.Any<CancellationToken>()).Returns(PaymentIntentState.Succeeded);
        _payment.ConfirmAsync("pi_1", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());

        (await Handler().Handle(new CancelMyOrderCommand(9), CancellationToken.None)).ErrorCode.Should().Be("OrderAlreadyPaid");

        order.Status.Should().Be(OrderStatus.Paid);
        await _reservations.Received(1).CommitAsync(OrderStockReference.For(9), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نيّة_أُلغيت_لدى_البوّابة_تُلغي_الطلب()
    {
        var order = Arrange(intent: "pi_1");
        _payment.CancelIntentAsync("pi_1", Arg.Any<CancellationToken>()).Returns(PaymentIntentState.Cancelled);

        (await Handler().Handle(new CancelMyOrderCommand(9), CancellationToken.None)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Cancelled);
    }
}
