using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Payments.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Payments;

// ============================================================================
// تنفيذ IOrderPayments (المرحلة 11). الاسترداد ثلاث خطوات، لا واحدة منها تمسك معاملة عبر الشبكة (ADR-0021):
//   (1) حجز المبلغ على الدفعة واسترداد معلّق — حفظ يحرسه rowversion الدفعة: طلبا استرداد متزامنان يُعاد ثانيهما من قراءة
//       جديدة فيرى ما حجزه الأول، ولا يتجاوز المجموع المدفوع؛
//   (2) البوّابة بمفتاح عدم تكرار من معرّف الاسترداد — إعادة المحاولة لا تردّ المال مرتين؛
//   (3) النتيجة في حفظ ثانٍ. انقطاع البوّابة في (2) يترك الاسترداد معلّقاً بمبلغه المحجوز حتى تُعاد المحاولة.
// ============================================================================
public sealed class OrderPayments : IOrderPayments
{
    public const int MaxAttempts = 5;
    private static readonly Error PaymentNotFound = Error.NotFound("لا دفعة لهذا الطلب");

    private readonly IPaymentRepository _payments;
    private readonly IPaymentService _gateway;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrderPayments> _logger;

    public OrderPayments(IPaymentRepository payments, IPaymentService gateway, ITenantContext tenant, IUnitOfWork uow,
                         TimeProvider clock, ILogger<OrderPayments> logger)
    {
        _payments = payments; _gateway = gateway; _tenant = tenant; _uow = uow; _clock = clock; _logger = logger;
    }

    public Task RecordIntentAsync(int orderId, PaymentIntentResult intent, Money amount, CancellationToken ct) =>
        _payments.AddAsync(new Payment(orderId, intent.Gateway, intent.PaymentIntentId, amount), ct);

    public async Task MarkSucceededAsync(int orderId, CancellationToken ct) =>
        (await _payments.GetForOrderAsync(orderId, ct))?.MarkSucceeded();

    public async Task MarkClosedAsync(int orderId, bool failed, CancellationToken ct)
    {
        var payment = await _payments.GetForOrderAsync(orderId, ct);
        if (payment is not { Status: PaymentStatus.Pending }) return;
        if (failed) payment.MarkFailed();
        else payment.MarkCancelled();
    }

    public async Task<bool> MarkCapturedAfterCloseAsync(int orderId, CancellationToken ct) =>
        (await _payments.GetForOrderAsync(orderId, ct))?.MarkCapturedAfterClose() ?? false;

    public async Task<Result<RefundOutcome>> RefundAsync(
        int orderId, decimal? amount, string? reason, int? requestedByUserId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var payment = await _payments.GetForOrderAsync(orderId, ct);
            if (payment is null) return Result<RefundOutcome>.Failure(PaymentNotFound);
            // "كل المتبقّي" (إلغاء طلب مدفوع) نتيجةٌ لا استثناء حين لا شيء يُردّ: الإلغاء نفسه نجح قبلها.
            if (amount is null && payment.Status != PaymentStatus.Succeeded)
                return Result<RefundOutcome>.Failure(Error.BusinessRule("PaymentNotRefundable", "لا يُستردّ إلا دفع ناجح"));
            if (amount is null && payment.Refundable.Amount == 0)
                return Result<RefundOutcome>.Failure(Error.BusinessRule("NothingToRefund", "لا مبلغ متبقٍّ للاسترداد"));

            var requested = amount is decimal value ? new Money(value, payment.Amount.Currency) : payment.Refundable;
            var refund = payment.RequestRefund(requested, reason, requestedByUserId);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                _payments.Reset();
                continue;
            }

            return await SendAsync(payment, refund, ct);
        }
    }

    public async Task<Result<RefundOutcome>> RetryRefundAsync(int orderId, int refundId, CancellationToken ct)
    {
        var payment = await _payments.GetForOrderAsync(orderId, ct);
        var refund = payment?.Refunds.FirstOrDefault(r => r.Id == refundId);
        if (payment is null || refund is null)
            return Result<RefundOutcome>.Failure(Error.NotFound("الاسترداد غير موجود"));
        if (refund.Status != RefundStatus.Pending)
            return Result<RefundOutcome>.Failure(Error.BusinessRule("RefundNotPending", "الاسترداد محسوم — لا يُعاد"));

        return await SendAsync(payment, refund, ct);
    }

    private async Task<Result<RefundOutcome>> SendAsync(Payment payment, Refund refund, CancellationToken ct)
    {
        PaymentRefundResult result;
        try
        {
            result = await _gateway.RefundAsync(payment.ProviderPaymentId, refund.Amount, IdempotencyKey(refund), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Refund {RefundId} of payment {PaymentId}: no answer from the gateway; left pending for retry",
                refund.Id, payment.Id);
            return Result<RefundOutcome>.Success(Outcome(refund));
        }

        return Result<RefundOutcome>.Success(Outcome(await ApplyAsync(payment, refund, result, ct)));
    }

    // الحفظ الثاني: نتيجة البوّابة على الاسترداد. تعارض (استرداد آخر غيّر الدفعة بينهما) ⇒ قراءة جديدة (الاسترداد محفوظ
    // فمعرّفه معروف) وتطبيق النتيجة نفسها.
    private async Task<Refund> ApplyAsync(Payment payment, Refund refund, PaymentRefundResult result, CancellationToken ct)
    {
        var (orderId, refundId) = (payment.OrderId, refund.Id);
        for (var attempt = 1; ; attempt++)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            if (result.Succeeded) payment.CompleteRefund(refund, result.ProviderRefundId ?? $"unrecorded-{refundId}", now);
            else payment.FailRefund(refund, result.FailureReason ?? "رفضت البوّابة الاسترداد", now);

            try
            {
                await _uow.SaveChangesAsync(ct);
                return refund;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                _payments.Reset();
                payment = await _payments.GetForOrderAsync(orderId, ct)
                          ?? throw new InvalidOperationException("اختفت الدفعة أثناء تسجيل نتيجة الاسترداد");
                refund = payment.Refunds.Single(r => r.Id == refundId);
            }
        }
    }

    private string IdempotencyKey(Refund refund) => $"souq-refund-{_tenant.RequireTenant().Id}-{refund.Id}";

    private static RefundOutcome Outcome(Refund refund) =>
        new(refund.Id, refund.Status.ToString(), refund.Amount.Amount, refund.Amount.Currency, refund.FailureReason);
}
