using Souq.Domain.ValueObjects;

namespace Souq.Application.Common.Interfaces;

// ============================================================================
// IPaymentService — العقد مع بوّابة الدفع. نُعرّفه هنا (في Application) لكن
// ننفّذه في Infrastructure (StripePaymentService مثلاً).
// الفائدة الذهبية: لتبديل Stripe بـ PayPal لاحقاً، نكتب تنفيذاً جديداً فقط،
// دون لمس أي منطق أعمال. هذا بالضبط "اعزل ما يتغيّر بسرعة" من الـ Roadmap.
// ============================================================================
public interface IPaymentService
{
    Task<PaymentResult> ChargeAsync(Money amount, string paymentToken, CancellationToken ct = default);
}

public record PaymentResult(bool Succeeded, string? TransactionId, string? FailureReason);
