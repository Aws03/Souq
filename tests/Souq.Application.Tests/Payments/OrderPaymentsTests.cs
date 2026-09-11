using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Payments;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Payments;

// ============================================================================
// دفعة الطلب واستردادها (المرحلة 11، ADR-0031): حجز المبلغ في حفظ، البوّابة بعده خارج أي معاملة بمفتاح عدم تكرار، ثم
// النتيجة في حفظ ثانٍ؛ انقطاع البوّابة يترك الاسترداد معلّقاً وإعادته بالمفتاح نفسه؛ وتعارض التزامن يُعاد من قراءة جديدة.
// ============================================================================
public class OrderPaymentsTests
{
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly IPaymentService _gateway = Substitute.For<IPaymentService>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    private OrderPayments CreateSut() =>
        new(_payments, _gateway, TestTenant.Context(), _uow, new FixedClock(), NullLogger<OrderPayments>.Instance);

    private static Payment Paid(decimal amount = 50m)
    {
        var payment = new Payment(orderId: 1, "fake", "pi_1", new Money(amount, "JOD"));
        payment.MarkSucceeded();
        return payment;
    }

    private void Stored(Payment first, params Payment[] later) =>
        _payments.GetForOrderAsync(1, Arg.Any<CancellationToken>()).Returns(first, later);

    private void GatewayAnswers(PaymentRefundResult result) =>
        _gateway.RefundAsync(default!, default!, default!, default).ReturnsForAnyArgs(result);

    [Fact]
    public async Task الاسترداد_يحجز_المبلغ_ثم_يسأل_البوّابة_خارج_أي_معاملة_ثم_يسجّل_نتيجتها()
    {
        var payment = Paid(50);
        Stored(payment);
        GatewayAnswers(new PaymentRefundResult(true, "re_1", null));

        var result = await CreateSut().RefundAsync(1, 20m, "منتج تالف", 7, CancellationToken.None);

        (result.Value!.Status, result.Value.Amount, result.Value.Currency).Should().Be(("Succeeded", 20m, "JOD"));
        var refund = payment.Refunds.Single();
        (payment.RefundedAmount, refund.ProviderRefundId, refund.RequestedByUserId, refund.Reason)
            .Should().Be((20m, "re_1", (int?)7, "منتج تالف"));
        Received.InOrder(() =>
        {
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            _gateway.RefundAsync("pi_1", Arg.Is<Money>(m => m.Amount == 20 && m.Currency == "JOD"), Arg.Any<string>(),
                Arg.Any<CancellationToken>());
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
        await _uow.DidNotReceiveWithAnyArgs().InTransactionAsync(default(Func<Task>)!, default);
    }

    [Fact]
    public async Task بلا_مبلغ_يُردّ_كل_المتبقّي()
    {
        var payment = Paid(50);
        payment.CompleteRefund(payment.RequestRefund(new Money(20, "JOD"), null, null), "re_0", DateTime.UtcNow);
        Stored(payment);
        GatewayAnswers(new PaymentRefundResult(true, "re_1", null));

        var result = await CreateSut().RefundAsync(1, null, null, 7, CancellationToken.None);

        result.Value!.Amount.Should().Be(30);
        payment.IsFullyRefunded.Should().BeTrue();
    }

    [Fact]
    public async Task رفض_البوّابة_يعلّم_الاسترداد_مرفوضاً_ويعيد_مبلغه_للمتبقّي()
    {
        var payment = Paid(50);
        Stored(payment);
        GatewayAnswers(new PaymentRefundResult(false, null, "رصيد الحساب غير كافٍ"));

        var result = await CreateSut().RefundAsync(1, 50m, null, 7, CancellationToken.None);

        (result.Value!.Status, result.Value.FailureReason).Should().Be(("Failed", "رصيد الحساب غير كافٍ"));
        (payment.Refundable.Amount, payment.RefundedAmount, payment.PendingRefundAmount).Should().Be((50m, 0m, 0m));
    }

    [Fact]
    public async Task انقطاع_البوّابة_يترك_الاسترداد_معلّقاً_وإعادته_تستخدم_المفتاح_نفسه()
    {
        var payment = Paid(50);
        Stored(payment);
        var keys = new List<string>();
        _gateway.RefundAsync("pi_1", Arg.Any<Money>(), Arg.Do<string>(keys.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PaymentRefundResult>(new HttpRequestException("timeout")),
                Task.FromResult(new PaymentRefundResult(true, "re_1", null)));

        var first = await CreateSut().RefundAsync(1, 20m, null, 7, CancellationToken.None);
        (first.Value!.Status, payment.PendingRefundAmount, payment.Refundable.Amount).Should().Be(("Pending", 20m, 30m));

        var retried = await CreateSut().RetryRefundAsync(1, first.Value.RefundId, CancellationToken.None);

        retried.Value!.Status.Should().Be("Succeeded");
        keys.Should().Equal($"souq-refund-1-{first.Value.RefundId}", $"souq-refund-1-{first.Value.RefundId}");
        (payment.RefundedAmount, payment.PendingRefundAmount).Should().Be((20m, 0m));
    }

    [Fact]
    public async Task تعارض_التزامن_يُعاد_من_قراءة_جديدة_فيرى_ما_حجزه_الطلب_الآخر()
    {
        var fresh = Paid(50);
        fresh.RequestRefund(new Money(40, "JOD"), null, null);   // طلب آخر حجز 40 والتزم أولاً
        Stored(Paid(50), fresh);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyConflictException()), Task.FromResult(1));

        var act = () => CreateSut().RefundAsync(1, 20m, null, 7, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidPaymentOperationException>()).Which.Code.Should().Be("RefundExceedsPayment");
        _payments.Received(1).Reset();
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task تعارض_عابر_يُعاد_وينجح()
    {
        var fresh = Paid(50);
        Stored(Paid(50), fresh);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyConflictException()), Task.FromResult(1));
        GatewayAnswers(new PaymentRefundResult(true, "re_1", null));

        var result = await CreateSut().RefundAsync(1, 20m, null, 7, CancellationToken.None);

        result.Value!.Status.Should().Be("Succeeded");
        fresh.RefundedAmount.Should().Be(20);
        _payments.Received(1).Reset();
    }

    [Fact]
    public async Task كل_المتبقّي_بلا_ما_يُردّ_نتيجة_لا_استثناء()
    {
        var pending = new Payment(1, "fake", "pi_1", new Money(50, "JOD"));
        var refunded = Paid(50);
        refunded.CompleteRefund(refunded.RequestRefund(new Money(50, "JOD"), null, null), "re_1", DateTime.UtcNow);
        _payments.GetForOrderAsync(1, Arg.Any<CancellationToken>()).Returns(pending);
        _payments.GetForOrderAsync(2, Arg.Any<CancellationToken>()).Returns(refunded);

        (await CreateSut().RefundAsync(1, null, null, 7, CancellationToken.None)).ErrorCode.Should().Be("PaymentNotRefundable");
        (await CreateSut().RefundAsync(2, null, null, 7, CancellationToken.None)).ErrorCode.Should().Be("NothingToRefund");
        (await CreateSut().RefundAsync(3, 5m, null, 7, CancellationToken.None)).ErrorCode.Should().Be("NotFound");
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task المبلغ_بعملة_الدفعة_وخاناتها()
    {
        Stored(Paid(50));

        var act = () => CreateSut().RefundAsync(1, 1.2345m, null, 7, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMoneyException>();
    }

    [Fact]
    public async Task إعادة_استرداد_محسوم_أو_مجهول_تُرفض()
    {
        var payment = Paid(50);
        var done = payment.RequestRefund(new Money(10, "JOD"), null, null);
        payment.CompleteRefund(done, "re_1", DateTime.UtcNow);
        Stored(payment);

        (await CreateSut().RetryRefundAsync(1, done.Id, CancellationToken.None)).ErrorCode.Should().Be("RefundNotPending");
        (await CreateSut().RetryRefundAsync(1, 999, CancellationToken.None)).ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task تسجيل_النيّة_بحسابها_والإلغاء_يحسم_المعلّقة_وحدها()
    {
        Payment? added = null;
        _payments.When(p => p.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())).Do(call => added = call.Arg<Payment>());

        await CreateSut().RecordIntentAsync(1, new PaymentIntentResult("pi_9", "secret", "stripe:store"), new Money(90, "JOD"),
            CancellationToken.None);
        (added!.Gateway, added.ProviderPaymentId, added.Amount.Amount, added.Status)
            .Should().Be(("stripe:store", "pi_9", 90m, PaymentStatus.Pending));

        var pending = new Payment(1, "fake", "pi_1", new Money(50, "JOD"));
        var paid = Paid(50);
        _payments.GetForOrderAsync(1, Arg.Any<CancellationToken>()).Returns(pending);
        _payments.GetForOrderAsync(2, Arg.Any<CancellationToken>()).Returns(paid);

        await CreateSut().MarkClosedAsync(1, failed: true, CancellationToken.None);
        await CreateSut().MarkClosedAsync(2, failed: false, CancellationToken.None);

        (pending.Status, paid.Status).Should().Be((PaymentStatus.Failed, PaymentStatus.Succeeded));
    }
}
