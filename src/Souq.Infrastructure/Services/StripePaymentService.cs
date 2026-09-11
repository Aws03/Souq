using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;
using Stripe;

namespace Souq.Infrastructure.Services;

// ============================================================================
// StripePaymentService — التنفيذ الحقيقي لبوّابة الدفع عبر Stripe. يُفعَّل تلقائياً
// في DI فقط حين يوجد Stripe:SecretKey مضبوطاً (وإلا FakePaymentService).
//
// عميل StripeClient خاص بهذه الخدمة بدل StripeConfiguration.ApiKey الساكن: الساكن
// حالة عامة مشتركة في كل العملية تمنع لاحقاً مفاتيح مختلفة لكل متجر (Phase 0 D2).
// التحقّق من توقيع الـ Webhook هنا أيضاً — لا تعرف أي طبقة أخرى صيغة Stripe (D1).
// ============================================================================
public class StripePaymentService : IPaymentService
{
    private const string OrderReferenceKey = "orderReference";

    private readonly StripeSettings _settings;
    private readonly IStripeClient _client;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(IOptions<StripeSettings> settings, ILogger<StripePaymentService> logger)
    {
        _settings = settings.Value;
        _client = new StripeClient(_settings.SecretKey);
        _logger = logger;
    }

    public async Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, CancellationToken ct = default)
    {
        var intent = await new PaymentIntentService(_client).CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = StripeAmountConverter.ToMinorUnits(amount),
            Currency = amount.Currency.ToLowerInvariant(),
            Metadata = new Dictionary<string, string> { [OrderReferenceKey] = orderReference },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
        }, cancellationToken: ct);

        return new PaymentIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct = default)
    {
        var intent = await new PaymentIntentService(_client).GetAsync(paymentIntentId, cancellationToken: ct);

        return intent.Status == "succeeded"
            ? new PaymentConfirmationResult(true, null)
            : new PaymentConfirmationResult(false, $"حالة الدفع لدى البوّابة: {intent.Status}");
    }

    public PaymentClientConfig GetClientConfig() =>
        new(string.IsNullOrWhiteSpace(_settings.PublishableKey) ? null : _settings.PublishableKey);

    public PaymentWebhookEvent? ParseWebhook(string payload, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookSecret))
        {
            _logger.LogWarning("وصل Webhook من Stripe لكن Stripe:WebhookSecret غير مضبوط — تم تجاهله");
            return null;
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader ?? "", _settings.WebhookSecret);
        }
        catch (StripeException ex)
        {
            throw new InvalidPaymentWebhookException(ex);
        }

        var isPaymentOutcome = stripeEvent.Type is "payment_intent.succeeded" or "payment_intent.payment_failed";
        return isPaymentOutcome
               && stripeEvent.Data.Object is PaymentIntent intent
               && intent.Metadata.TryGetValue(OrderReferenceKey, out var reference)
            ? new PaymentWebhookEvent(reference)
            : null;
    }
}
