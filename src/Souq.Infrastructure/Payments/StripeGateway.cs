using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Services;
using Stripe;

namespace Souq.Infrastructure.Payments;

public sealed record StripeCredentials(string SecretKey, string? PublishableKey, string? WebhookSecret);

// ============================================================================
// StripeGateway — Stripe بحساب واحد (حساب النشر أو حساب متجر). عميل StripeClient خاص بالمحوّل لا StripeConfiguration
// الساكن: مفاتيح مختلفة لكل متجر في العملية نفسها (Phase 0 D2). نيّة الدفع تحمل مرجع الطلب والمتجر في بياناتها الوصفية —
// الإشعار يُوجَّه بها لمتجره (المرحلة 11). التحقّق من التوقيع هنا وحده: لا طبقة أخرى تعرف صيغة Stripe (D1).
// ============================================================================
public sealed class StripeGateway : IPaymentGateway
{
    public const string StoreAccount = "stripe:store";
    public const string DeploymentAccount = "stripe:deployment";
    private const string OrderReferenceKey = "orderReference";
    private const string TenantKey = "tenantId";

    private readonly StripeCredentials _credentials;
    private readonly IStripeClient _client;
    private readonly ILogger _logger;

    public StripeGateway(string name, StripeCredentials credentials, ILogger logger)
    {
        Name = name;
        _credentials = credentials;
        _client = new StripeClient(credentials.SecretKey);
        _logger = logger;
    }

    public string Name { get; }

    public string? PublishableKey => string.IsNullOrWhiteSpace(_credentials.PublishableKey) ? null : _credentials.PublishableKey;

    public bool CanVerifyWebhooks => !string.IsNullOrWhiteSpace(_credentials.WebhookSecret);

    public async Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, int tenantId, CancellationToken ct)
    {
        var intent = await new PaymentIntentService(_client).CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = StripeAmountConverter.ToMinorUnits(amount),
            Currency = amount.Currency.ToLowerInvariant(),
            Metadata = new Dictionary<string, string>
            {
                [OrderReferenceKey] = orderReference,
                [TenantKey] = tenantId.ToString(CultureInfo.InvariantCulture),
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            // مفتاح عدم تكرار مشتقّ من الطلب: انقطاع الشبكة بعد أن أنشأت Stripe النيّة وقبل وصول جوابها كان يترك نيّة
            // يتيمة لا نعرف معرّفها؛ إعادة المحاولة بالمفتاح نفسه تعيد النيّة الأولى بدل إنشاء ثانية.
        }, new RequestOptions { IdempotencyKey = $"souq-intent-{tenantId}-{orderReference}" }, ct);

        return new PaymentIntentResult(intent.Id, intent.ClientSecret, Name, PublishableKey);
    }

    // الحالة كما هي، لا "نجح/لم ينجح": requires_payment_method بعد رفض البطاقة نيّة حيّة يعيد العميل المحاولة عليها،
    // وليست فشلاً نهائياً يُلغى طلبه (R-02). ما ليس حالة نهائية معروفة ⇒ Retryable.
    public async Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct)
    {
        var intent = await new PaymentIntentService(_client).GetAsync(paymentIntentId, cancellationToken: ct);
        var state = StateOf(intent.Status) ?? PaymentIntentState.Retryable;

        return new PaymentConfirmationResult(state,
            state == PaymentIntentState.Succeeded ? null : $"حالة الدفع لدى البوّابة: {intent.Status}");
    }

    public async Task<PaymentIntentState> CancelIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        var service = new PaymentIntentService(_client);
        var intent = await service.GetAsync(paymentIntentId, cancellationToken: ct);
        if (StateOf(intent.Status) is { } settled) return settled;

        try
        {
            await service.CancelAsync(paymentIntentId, cancellationToken: ct);
            return PaymentIntentState.Cancelled;
        }
        catch (StripeException ex)
        {
            // سباق: تغيّرت الحالة بين القراءة والإلغاء (دفع العميل للتوّ) — الحالة الحقيقية تحسم.
            _logger.LogWarning(ex, "Stripe refused to cancel payment intent {PaymentIntentId}; re-reading its state", paymentIntentId);
            intent = await service.GetAsync(paymentIntentId, cancellationToken: ct);
            return StateOf(intent.Status) ?? PaymentIntentState.Processing;
        }
    }

    // مفتاح عدم التكرار (Idempotency-Key) يجعل إعادة الطلب نفسه تعيد الاسترداد الأول لا استرداداً ثانياً. pending نجاح:
    // Stripe قبل الاسترداد وسيعكسه على البطاقة. رفض صريح (مبلغ يتجاوز، دفعة مستردّة، نيّة مجهولة) نتيجة لا استثناء؛ غير
    // ذلك (انقطاع، 5xx) يرتفع — النتيجة مجهولة ويبقى الاسترداد معلّقاً لإعادته بالمفتاح نفسه.
    public async Task<PaymentRefundResult> RefundAsync(string paymentIntentId, Money amount, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var refund = await new RefundService(_client).CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId,
                Amount = StripeAmountConverter.ToMinorUnits(amount),
                Reason = "requested_by_customer",
            }, new RequestOptions { IdempotencyKey = idempotencyKey }, ct);

            return refund.Status is "succeeded" or "pending"
                ? new PaymentRefundResult(true, refund.Id, null)
                : new PaymentRefundResult(false, refund.Id, $"حالة الاسترداد لدى البوّابة: {refund.Status}");
        }
        catch (StripeException ex) when (ex.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.PaymentRequired
                                             or HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Stripe refused a refund of payment intent {PaymentIntentId}", paymentIntentId);
            return new PaymentRefundResult(false, null, ex.StripeError?.Message ?? "رفضت البوّابة الاسترداد");
        }
    }

    public GatewayWebhookEvent? ParseWebhook(string payload, string? signatureHeader)
    {
        if (!CanVerifyWebhooks) return null;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader ?? "", _credentials.WebhookSecret);
        }
        catch (StripeException ex)
        {
            throw new InvalidPaymentWebhookException(ex);
        }

        if (stripeEvent.Type is not ("payment_intent.succeeded" or "payment_intent.payment_failed")
            || stripeEvent.Data.Object is not PaymentIntent intent
            || !intent.Metadata.TryGetValue(OrderReferenceKey, out var reference))
            return null;

        int? tenantId = intent.Metadata.TryGetValue(TenantKey, out var raw)
                        && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        return new GatewayWebhookEvent(intent.Id, reference, tenantId);
    }

    // حالات نهائية أو غير قابلة للإلغاء الآن؛ null ⇒ قابلة للإلغاء (requires_payment_method/confirmation/action/capture).
    private static PaymentIntentState? StateOf(string status) => status switch
    {
        "succeeded" => PaymentIntentState.Succeeded,
        "canceled" => PaymentIntentState.Cancelled,
        "processing" => PaymentIntentState.Processing,
        _ => null,
    };
}
