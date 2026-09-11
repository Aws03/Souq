using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// منسّق انتهاء مهلة الدفع (Phase 0 C6): لا يُلغى طلب قد يكون دُفع — البوّابة تُسأل أولاً (بإلغاء النيّة)، ولكل مرجع
// مصيره المستقلّ.
public class ExpireStaleCheckoutsHandlerTests
{
    private readonly IInventoryReservations _reservations = Substitute.For<IInventoryReservations>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    private ExpireStaleCheckoutsHandler CreateHandler() => new(
        _reservations, _orders, _payment,
        new OrderPaymentConfirmation(_orders, _reservations, Substitute.For<ICustomerRepository>(),
            Substitute.For<ICouponRepository>(), _payment, Substitute.For<IEmailService>(), _uow),
        NullLogger<ExpireStaleCheckoutsHandler>.Instance);

    private Order PendingOrder(int id, string? intent = null)
    {
        var order = TestCatalog.WithId(new Order(1, "عمّان", "JOD"), id);
        order.AddItem(1, "سماعات", new Money(10, "JOD"), 1);
        if (intent is not null) order.SetPaymentIntent(intent);
        _orders.GetWithItemsAsync(id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private void Expired(params string[] references) =>
        _reservations.FindExpiredAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(references);

    private Task<int> Sweep() => CreateHandler().Handle(new ExpireStaleCheckoutsCommand(), CancellationToken.None);

    [Fact]
    public async Task طلب_معلّق_بلا_نيّة_دفع_يُلغى_ويُعلَّم_حجزه_منتهياً()
    {
        var order = PendingOrder(5);
        Expired(OrderStockReference.For(5));

        (await Sweep()).Should().Be(1);

        order.Status.Should().Be(OrderStatus.Cancelled);
        await _reservations.Received(1).CancelAsync(OrderStockReference.For(5), "انتهت مهلة الدفع", true, Arg.Any<CancellationToken>());
        await _payment.DidNotReceive().CancelIntentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نيّة_دفع_ألغتها_البوّابة_يُلغى_طلبها()
    {
        var order = PendingOrder(5, "pi_1");
        Expired(OrderStockReference.For(5));
        _payment.CancelIntentAsync("pi_1", Arg.Any<CancellationToken>()).Returns(PaymentIntentState.Cancelled);

        await Sweep();

        order.Status.Should().Be(OrderStatus.Cancelled);
        await _reservations.Received(1).CancelAsync(OrderStockReference.For(5), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نيّة_نجحت_قبل_الإلغاء_يُؤكَّد_طلبها_ويُلتزم_حجزه_لا_يُلغى()
    {
        var order = PendingOrder(5, "pi_1");
        Expired(OrderStockReference.For(5));
        _payment.CancelIntentAsync("pi_1", Arg.Any<CancellationToken>()).Returns(PaymentIntentState.Succeeded);
        _payment.ConfirmAsync("pi_1", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));

        await Sweep();

        order.Status.Should().Be(OrderStatus.Paid);
        await _reservations.Received(1).CommitAsync(OrderStockReference.For(5), Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نيّة_قيد_المعالجة_تُترك_للدورة_التالية()
    {
        var order = PendingOrder(5, "pi_1");
        Expired(OrderStockReference.For(5));
        _payment.CancelIntentAsync("pi_1", Arg.Any<CancellationToken>()).Returns(PaymentIntentState.Processing);

        (await Sweep()).Should().Be(0);

        order.Status.Should().Be(OrderStatus.Pending);
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task حجز_بلا_طلب_يُحرَّر_وحجز_نشط_لطلب_مدفوع_يُلتزم()
    {
        _orders.GetWithItemsAsync(9, Arg.Any<CancellationToken>()).Returns((Order?)null);
        var paid = PendingOrder(6);
        paid.MarkAsPaid();
        Expired(OrderStockReference.For(9), OrderStockReference.For(6), "not-an-order");

        (await Sweep()).Should().Be(3);

        await _reservations.Received(1).CancelAsync(OrderStockReference.For(9), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        await _reservations.Received(1).CancelAsync("not-an-order", Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        await _reservations.Received(1).CommitAsync(OrderStockReference.For(6), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task فشل_مرجع_لا_يوقف_البقية()
    {
        _orders.GetWithItemsAsync(5, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());
        var second = PendingOrder(6);
        Expired(OrderStockReference.For(5), OrderStockReference.For(6));

        (await Sweep()).Should().Be(1);

        second.Status.Should().Be(OrderStatus.Cancelled);
    }
}
