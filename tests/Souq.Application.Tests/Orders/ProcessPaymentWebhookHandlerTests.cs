using AwesomeAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Orders.Commands;

namespace Souq.Application.Tests.Orders;

// الـ Webhook يمرّ عبر منفذ الدفع (التحقّق من التوقيع هناك) ثم لنفس أمر التأكيد.
public class ProcessPaymentWebhookHandlerTests
{
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly ISender _sender = Substitute.For<ISender>();

    private ProcessPaymentWebhookHandler CreateHandler() => new(_payment, _sender);

    [Fact]
    public async Task توقيع_غير_صالح_يُرفض_ولا_يُلمس_أي_طلب()
    {
        _payment.ParseWebhook(Arg.Any<string>(), Arg.Any<string?>()).Throws(new InvalidPaymentWebhookException());

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "bad"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidSignature");
        await _sender.DidNotReceive().Send(Arg.Any<ConfirmOrderPaymentCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task حدث_لا_يخصّ_دفعة_طلب_يُقرّ_به_بلا_فعل()
    {
        _payment.ParseWebhook(Arg.Any<string>(), Arg.Any<string?>()).Returns((PaymentWebhookEvent?)null);

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _sender.DidNotReceive().Send(Arg.Any<ConfirmOrderPaymentCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task حدث_دفعة_يمرّر_إلى_أمر_التأكيد_نفسه()
    {
        _payment.ParseWebhook("{}", "sig").Returns(new PaymentWebhookEvent("42"));

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _sender.Received(1).Send(
            Arg.Is<ConfirmOrderPaymentCommand>(c => c.OrderId == 42), Arg.Any<CancellationToken>());
    }
}
