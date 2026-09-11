using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// الدفعة واستردادها (المرحلة 11، ADR-0031): الحسم مضمون التكرار ولا يقفز بين حالتين محسومتين؛ مجموع المسترَدّ والمعلّق لا
// يتجاوز المدفوع أبداً؛ الاسترداد المرفوض يعيد مبلغه للمتبقّي؛ وتكرار نتيجة البوّابة لا يعدّ المال مرتين.
// ============================================================================
public class PaymentTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private static Payment Pending(decimal amount = 50m, string currency = "JOD") =>
        new(orderId: 1, "fake", $"pi_{Guid.NewGuid():N}", new Money(amount, currency));

    private static Payment Paid(decimal amount = 50m)
    {
        var payment = Pending(amount);
        payment.MarkSucceeded();
        return payment;
    }

    [Fact]
    public void الدفعة_تبدأ_معلّقة_وحسمها_مضمون_التكرار_ولا_تنتقل_بين_حالتين_محسومتين()
    {
        var paid = Pending();
        paid.Status.Should().Be(PaymentStatus.Pending);
        paid.MarkSucceeded();
        paid.MarkSucceeded();
        paid.Status.Should().Be(PaymentStatus.Succeeded);
        paid.Invoking(p => p.MarkCancelled()).Should().Throw<InvalidPaymentOperationException>();
        paid.Invoking(p => p.MarkFailed()).Should().Throw<InvalidPaymentOperationException>();

        var failed = Pending();
        failed.MarkFailed();
        failed.MarkFailed();
        failed.Invoking(p => p.MarkSucceeded()).Should().Throw<InvalidPaymentOperationException>();

        var cancelled = Pending();
        cancelled.MarkCancelled();
        cancelled.Status.Should().Be(PaymentStatus.Cancelled);
    }

    [Theory]
    [InlineData(0, "fake", "pi_1")]
    [InlineData(1, "", "pi_1")]
    [InlineData(1, "fake", " ")]
    public void الدفعة_تخصّ_طلباً_محفوظاً_وحساباً_ونيّة(int orderId, string gateway, string providerId)
    {
        var act = () => new Payment(orderId, gateway, providerId, new Money(10, "JOD"));

        act.Should().Throw<InvalidPaymentOperationException>();
    }

    [Fact]
    public void الاستردادات_لا_تتجاوز_المدفوع_والمعلّق_يُحجز_من_المتبقّي()
    {
        var payment = Paid(50);

        var first = payment.RequestRefund(new Money(30, "JOD"), "منتج تالف", 7);
        (first.Status, first.Reason, first.RequestedByUserId, payment.Refundable).Should()
            .Be((RefundStatus.Pending, "منتج تالف", 7, new Money(20, "JOD")));

        payment.Invoking(p => p.RequestRefund(new Money(20.001m, "JOD"), null, 7))
            .Should().Throw<InvalidPaymentOperationException>().Which.Code.Should().Be("RefundExceedsPayment");

        payment.CompleteRefund(first, "re_1", Now);
        (payment.RefundedAmount, payment.PendingRefundAmount, payment.Refundable.Amount, first.CompletedAt)
            .Should().Be((30m, 0m, 20m, (DateTime?)Now));

        var second = payment.RequestRefund(new Money(20, "JOD"), null, 7);
        payment.CompleteRefund(second, "re_2", Now);
        (payment.IsFullyRefunded, payment.Refundable.Amount, payment.Status).Should().Be((true, 0m, PaymentStatus.Succeeded));
        payment.Invoking(p => p.RequestRefund(new Money(0.001m, "JOD"), null, 7)).Should().Throw<InvalidPaymentOperationException>();
    }

    [Fact]
    public void الاسترداد_المرفوض_يعيد_مبلغه_للمتبقّي_وتكرار_النتيجة_لا_يعدّ_مرتين()
    {
        var payment = Paid(50);

        var refused = payment.RequestRefund(new Money(50, "JOD"), null, null);
        payment.FailRefund(refused, "رصيد غير كافٍ", Now);
        payment.FailRefund(refused, "مكرّر", Now);
        (refused.Status, refused.FailureReason, payment.Refundable.Amount, payment.RefundedAmount)
            .Should().Be((RefundStatus.Failed, "رصيد غير كافٍ", 50m, 0m));
        payment.Invoking(p => p.CompleteRefund(refused, "re_x", Now)).Should().Throw<InvalidPaymentOperationException>();

        var accepted = payment.RequestRefund(new Money(10, "JOD"), null, null);
        payment.CompleteRefund(accepted, "re_1", Now);
        payment.CompleteRefund(accepted, "re_1", Now);
        (payment.RefundedAmount, payment.PendingRefundAmount).Should().Be((10m, 0m));
        payment.Invoking(p => p.FailRefund(accepted, "متأخّر", Now)).Should().Throw<InvalidPaymentOperationException>();
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    public void لا_يُستردّ_إلا_دفع_ناجح(PaymentStatus status)
    {
        var payment = Pending();
        if (status == PaymentStatus.Failed) payment.MarkFailed();
        if (status == PaymentStatus.Cancelled) payment.MarkCancelled();

        payment.Invoking(p => p.RequestRefund(new Money(1, "JOD"), null, null))
            .Should().Throw<InvalidPaymentOperationException>().Which.Code.Should().Be("PaymentNotRefundable");
    }

    [Fact]
    public void الاسترداد_بعملة_الدفعة_وبمبلغ_موجب_ولا_يُحسم_على_دفعة_أخرى()
    {
        var payment = Paid(50);
        var other = Paid(50);

        payment.Invoking(p => p.RequestRefund(new Money(5, "USD"), null, null)).Should().Throw<InvalidPaymentOperationException>();
        payment.Invoking(p => p.RequestRefund(new Money(0, "JOD"), null, null)).Should().Throw<InvalidPaymentOperationException>();

        var refund = payment.RequestRefund(new Money(1, "JOD"), null, null);
        other.Invoking(p => p.CompleteRefund(refund, "re_1", Now)).Should().Throw<InvalidPaymentOperationException>();
        other.RefundedAmount.Should().Be(0);
    }

    [Fact]
    public void سبب_الاسترداد_يُقتطع_لحدّه_والفارغ_لا_يُحفظ()
    {
        var payment = Paid(50);

        var longReason = payment.RequestRefund(new Money(1, "JOD"), new string('س', Refund.ReasonMaxLength + 20), null);
        var blank = payment.RequestRefund(new Money(1, "JOD"), "   ", null);

        (longReason.Reason!.Length, blank.Reason).Should().Be((Refund.ReasonMaxLength, (string?)null));
    }
}
