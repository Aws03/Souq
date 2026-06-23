using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Services;

// ============================================================================
// FakePaymentService — تنفيذ تجريبي لبوّابة الدفع للتطوير/التعلّم.
// الفكرة الهندسية الأهم: طبقة Application لا تعرف أن هذا "وهمي". تتعامل مع
// IPaymentService فقط. عند الانتقال للإنتاج نكتب StripePaymentService ينفّذ
// نفس الواجهة، ونبدّله بسطر واحد في DI — دون لمس أي منطق أعمال.
// هذا هو العائد العملي لمبدأ "اعزل ما يتغيّر بسرعة".
// ============================================================================
public class FakePaymentService : IPaymentService
{
    public Task<PaymentResult> ChargeAsync(Money amount, string paymentToken, CancellationToken ct = default)
    {
        // قاعدة محاكاة: أي رمز يبدأ بـ "fail" يُحاكي فشل الدفع (لاختبار المسار).
        if (paymentToken.StartsWith("fail", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new PaymentResult(false, null, "رُفضت البطاقة"));

        return Task.FromResult(new PaymentResult(true, Guid.NewGuid().ToString(), null));
    }
}
