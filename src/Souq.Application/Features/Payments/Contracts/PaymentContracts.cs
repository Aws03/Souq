using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Payments.Contracts;

// ============================================================================
// عقود وحدة Payments (المرحلة 11، ADR-0031). Ordering يسجّل دفعة طلبه ويحسمها ويستردّها عبر IOrderPayments — لا يعرف
// Payment ولا البوّابة. التسجيل والحسم يُتتبَّعان في وحدة عمل المستدعي (يُحفظان مع الطلب في معاملته). الاسترداد يدير
// حفظَيه بنفسه لأن بينهما استدعاء البوّابة (ADR-0021) — فلا يُستدعى داخل معاملة مفتوحة.
// ============================================================================
public interface IOrderPayments
{
    Task RecordIntentAsync(int orderId, PaymentIntentResult intent, Money amount, CancellationToken ct);

    Task MarkSucceededAsync(int orderId, CancellationToken ct);

    // دفعة معلّقة لطلب أُلغي: failed لرفض البوّابة، وإلا ملغاة. دفعة محسومة لا تتغيّر.
    Task MarkClosedAsync(int orderId, bool failed, CancellationToken ct);

    // amount null ⇒ كل المتبقّي. بعملة الدفعة وخاناتها. البوّابة لم تُجب ⇒ استرداد معلّق (يُعاد بالمفتاح نفسه).
    Task<Result<RefundOutcome>> RefundAsync(int orderId, decimal? amount, string? reason, int? requestedByUserId, CancellationToken ct);

    Task<Result<RefundOutcome>> RetryRefundAsync(int orderId, int refundId, CancellationToken ct);
}

// الدفعة كما تُعرض مع الطلب (قراءة).
public interface IPaymentQueries
{
    Task<OrderPaymentDto?> ForOrderAsync(int orderId, CancellationToken ct);
}

public record RefundOutcome(int RefundId, string Status, decimal Amount, string Currency, string? FailureReason);

public record RefundDto(
    int Id, decimal Amount, string Status, string? Reason, string? FailureReason, DateTime CreatedAt, DateTime? CompletedAt);

public record OrderPaymentDto(
    string Status, decimal Amount, decimal RefundedAmount, decimal Refundable, string Currency, List<RefundDto> Refunds);
