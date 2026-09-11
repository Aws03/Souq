using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// الـ Webhook يمرّ عبر منفذ الدفع (التحقّق من التوقيع هناك) ثم لنفس منطق التأكيد الذي
// يستخدمه العميل — بلا مستخدم خلفه: التفويض هو التوقيع لا فحص الملكية.
public class ProcessPaymentWebhookHandlerTests
{
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _uow = Souq.Application.Tests.TestDoubles.TestUnitOfWork.Create();
    private readonly Souq.Application.Features.Inventory.Contracts.IInventoryReservations _reservations =
        Substitute.For<Souq.Application.Features.Inventory.Contracts.IInventoryReservations>();

    private ProcessPaymentWebhookHandler CreateHandler() => new(
        _payment, _orders,
        new OrderPaymentConfirmation(_orders, _reservations,
            Substitute.For<ICustomerRepository>(), Substitute.For<ICouponRepository>(),
            _payment, Substitute.For<IEmailService>(), _uow),
        NullLogger<ProcessPaymentWebhookHandler>.Instance);

    [Fact]
    public async Task توقيع_غير_صالح_يُرفض_ولا_يُلمس_أي_طلب()
    {
        _payment.ParseWebhook(Arg.Any<string>(), Arg.Any<string?>()).Throws(new InvalidPaymentWebhookException());

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "bad"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidSignature");
        await _orders.DidNotReceive().GetWithItemsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task حدث_لا_يخصّ_دفعة_طلب_يُقرّ_به_بلا_فعل()
    {
        _payment.ParseWebhook(Arg.Any<string>(), Arg.Any<string?>()).Returns((PaymentWebhookEvent?)null);

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _orders.DidNotReceive().GetWithItemsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task حدث_دفعة_يؤكّد_الطلب_بعد_التحقّق_لدى_البوّابة_بلا_أي_مستخدم()
    {
        var order = new Order(customerId: 7, "عمّان", "JOD");
        order.AddItem(1, "سماعات", new Money(50, "JOD"), 1);
        order.SetPaymentIntent("pi_42");
        _payment.ParseWebhook("{}", "sig").Returns(new PaymentWebhookEvent("42"));
        _orders.GetWithItemsAsync(42, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_42", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _reservations.Received(1).CommitAsync(OrderStockReference.For(order.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task مرجع_طلب_مجهول_يُقرّ_به_بلا_استدعاء_البوّابة()
    {
        _payment.ParseWebhook("{}", "sig").Returns(new PaymentWebhookEvent("404"));
        _orders.GetWithItemsAsync(404, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _payment.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
