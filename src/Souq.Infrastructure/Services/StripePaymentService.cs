using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;
using Stripe;

namespace Souq.Infrastructure.Services;

// ============================================================================
// StripePaymentService — التنفيذ الحقيقي لبوّابة الدفع عبر Stripe. يُفعَّل
// تلقائياً في DI فقط حين يوجد Stripe:SecretKey مضبوطاً (وإلا FakePaymentService).
// ============================================================================
public class StripePaymentService : IPaymentService
{
    public StripePaymentService(IOptions<StripeSettings> settings)
    {
        StripeConfiguration.ApiKey = settings.Value.SecretKey;
    }

    public async Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, CancellationToken ct = default)
    {
        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = ToSmallestUnit(amount),
            Currency = amount.Currency.ToLowerInvariant(),
            Metadata = new Dictionary<string, string> { ["orderReference"] = orderReference },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
        }, cancellationToken: ct);

        return new PaymentIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct = default)
    {
        var service = new PaymentIntentService();
        var intent = await service.GetAsync(paymentIntentId, cancellationToken: ct);

        return intent.Status == "succeeded"
            ? new PaymentConfirmationResult(true, null)
            : new PaymentConfirmationResult(false, $"حالة الدفع لدى البوّابة: {intent.Status}");
    }

    // Stripe يتوقّع أصغر وحدة عملة (مثل السنت) لا رقماً عشرياً. ملاحظة: الدينار
    // الأردني عملة ثلاثية الخانات العشرية (الفلس) بينما Stripe يتعامل بخانتين
    // كمعظم العملات — نتقرّب لأقرب فلسين (٠.٠١ د.أ) عند حدود بوّابة الدفع، وهو
    // تقريب موثَّق ومقبول لعملة نادرة الدعم لا حلّ مثالياً له بلا تكوين خاص.
    private static long ToSmallestUnit(Money amount) => (long)Math.Round(amount.Amount * 100m, MidpointRounding.AwayFromZero);
}
