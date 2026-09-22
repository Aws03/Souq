using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Payments;

// ============================================================================
// FakeGateway — البوّابة التجريبية (تطوير واختبار، أو Payments:Provider=Fake صراحةً مع تحذير): كل دفع ينجح بلا مال.
// الاسترداد يُسجَّل في دفتر بمفتاح عدم التكرار — إعادة المفتاح نفسه تعيد الاسترداد نفسه كما تفعل Stripe، فتُختبر عدم
// الازدواجية فعلاً. الإشعارات تُقبل موقَّعة بـ HMAC-SHA256 بسرّ Payments:Fake:WebhookSecret إن ضُبط (التطوير والاختبارات
// وحدها — حساب حقيقي يستخدم Stripe وتوقيعها).
// ============================================================================
public sealed class FakeGatewayLedger
{
    private readonly ConcurrentDictionary<string, string> _refunds = new();

    public string Refund(string idempotencyKey) => _refunds.GetOrAdd(idempotencyKey, _ => $"re_fake_{Guid.NewGuid():N}");

    public int RefundsFor(string idempotencyKeyPrefix) => _refunds.Keys.Count(k => k.StartsWith(idempotencyKeyPrefix, StringComparison.Ordinal));
}

public sealed class FakeGateway : IPaymentGateway
{
    public const string GatewayName = "fake";
    public const string SignaturePrefix = "sha256=";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly FakeGatewayLedger _ledger;
    private readonly string? _webhookSecret;

    public FakeGateway(FakeGatewayLedger ledger, string? webhookSecret)
    {
        _ledger = ledger;
        _webhookSecret = string.IsNullOrWhiteSpace(webhookSecret) ? null : webhookSecret;
    }

    public string Name => GatewayName;

    // لا مفتاح علني: الواجهة تعرض زرّ إتمام مباشر بدل نموذج البطاقة.
    public string? PublishableKey => null;

    public bool CanVerifyWebhooks => _webhookSecret is not null;

    public Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, int tenantId, CancellationToken ct)
    {
        var id = $"pi_fake_{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentIntentResult(id, $"{id}_secret_fake", Name, PublishableKey));
    }

    public Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct) =>
        Task.FromResult(PaymentConfirmationResult.Ok());

    // لا نيّة حقيقية تُلغى: "الدفع" هنا زرّ الإتمام، فطلب هُجر قبله يُلغى محلياً عند انتهاء مهلته.
    public Task<PaymentIntentState> CancelIntentAsync(string paymentIntentId, CancellationToken ct) =>
        Task.FromResult(PaymentIntentState.Cancelled);

    public Task<PaymentRefundResult> RefundAsync(string paymentIntentId, Money amount, string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(new PaymentRefundResult(true, _ledger.Refund(idempotencyKey), null));

    public GatewayWebhookEvent? ParseWebhook(string payload, string? signatureHeader)
    {
        if (_webhookSecret is null) return null;
        var expected = Encoding.ASCII.GetBytes(Sign(payload, _webhookSecret));
        var given = Encoding.ASCII.GetBytes(signatureHeader ?? "");
        if (!CryptographicOperations.FixedTimeEquals(expected, given))
            throw new InvalidPaymentWebhookException(new CryptographicException("توقيع الإشعار التجريبي غير صالح"));

        var body = JsonSerializer.Deserialize<FakeWebhookBody>(payload, Json);
        return body is { Type: "payment_intent.succeeded" or "payment_intent.payment_failed", OrderReference: { Length: > 0 } reference }
            ? new GatewayWebhookEvent(body.PaymentIntentId ?? "", reference, body.TenantId)
            : null;
    }

    public static string Sign(string payload, string secret) =>
        SignaturePrefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

    private sealed record FakeWebhookBody(string? Type, string? PaymentIntentId, string? OrderReference, int? TenantId);
}
