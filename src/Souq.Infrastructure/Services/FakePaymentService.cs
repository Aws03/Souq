using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Services;

// ============================================================================
// FakePaymentService — تنفيذ تجريبي لبوّابة الدفع، يُستخدم تلقائياً حين لا يوجد
// مفتاح Stripe مضبوط (تطوير محلي بلا حساب Stripe). لا محاكاة فشل هنا عمداً:
// اختبار مسار الفشل الحقيقي يكون عبر بطاقات Stripe التجريبية المُوثَّقة.
// لا Webhooks للبوّابة التجريبية، ولا مفتاح علني (الواجهة تعرض زرّ إتمام مباشر).
// ============================================================================
public class FakePaymentService : IPaymentService
{
    public Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, CancellationToken ct = default)
    {
        var id = $"pi_fake_{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentIntentResult(id, $"{id}_secret_fake"));
    }

    public Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct = default)
        => Task.FromResult(new PaymentConfirmationResult(true, null));

    public PaymentClientConfig GetClientConfig() => new(PublishableKey: null);

    public PaymentWebhookEvent? ParseWebhook(string payload, string? signatureHeader) => null;
}
