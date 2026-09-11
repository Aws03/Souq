using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Refund — استرداد جزئي أو كامل لدفعة (المرحلة 11). يُنشأ معلّقاً قبل استدعاء البوّابة (في معاملة تحجز مبلغه من الدفعة)،
// ثم تُسجَّل نتيجتها في معاملة ثانية — لا استدعاء شبكة داخل معاملة (ADR-0021). معرّفه يصنع مفتاح عدم التكرار لدى
// البوّابة، فإعادة استرداد معلّق لا تردّ المال مرتين. لا يُنشأ ولا يُحسم إلا عبر Payment (حارس المجموع هناك).
// ============================================================================
public class Refund : Entity, ITenantOwned
{
    public const int ReasonMaxLength = 500;
    public const int ProviderRefundIdMaxLength = 100;

    public int TenantId { get; private set; }
    public int PaymentId { get; private set; }
    public Money Amount { get; private set; } = default!;
    public string? Reason { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public string? FailureReason { get; private set; }
    public int? RequestedByUserId { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private Refund() { }

    internal Refund(Money amount, string? reason, int? requestedByUserId)
    {
        Amount = amount;
        Reason = Trim(reason);
        RequestedByUserId = requestedByUserId;
        Status = RefundStatus.Pending;
    }

    internal bool Complete(string providerRefundId, DateTime now)
    {
        if (Status == RefundStatus.Succeeded) return false;
        if (Status == RefundStatus.Failed)
            throw new InvalidPaymentOperationException("استرداد مرفوض لا يُعلَّم ناجحاً — اطلب استرداداً جديداً");
        if (string.IsNullOrWhiteSpace(providerRefundId) || providerRefundId.Length > ProviderRefundIdMaxLength)
            throw new InvalidPaymentOperationException("معرّف الاسترداد لدى البوّابة مطلوب");

        Status = RefundStatus.Succeeded;
        ProviderRefundId = providerRefundId;
        CompletedAt = now;
        return true;
    }

    internal bool Fail(string reason, DateTime now)
    {
        if (Status == RefundStatus.Failed) return false;
        if (Status == RefundStatus.Succeeded)
            throw new InvalidPaymentOperationException("استرداد ناجح لا يُعلَّم مرفوضاً");

        Status = RefundStatus.Failed;
        FailureReason = Trim(reason) ?? "رفضت البوّابة الاسترداد";
        CompletedAt = now;
        return true;
    }

    private static string? Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, ReasonMaxLength)];
}
