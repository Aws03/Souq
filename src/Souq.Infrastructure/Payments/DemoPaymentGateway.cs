using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Payments;

// ============================================================================
// بوّابةُ الدفع التجريبية — **محوّلُ العرض لهذا المستودع** (ADR-0063).
//
// **لا يتحرّك بها مالٌ أبداً، ولا تلمس بطاقةً، ولا تحفظ اعتماداً.** هي تنفيذٌ محلّيٌّ حتميّ
// خلف `IPaymentGateway` نفسه الذي ينفّذه مزوّدٌ حقيقي — أي أنّ إدخال مزوّدٍ فعليّ تغييرُ محوّلٍ
// واحد، لا تغييرُ واجهةٍ في Application.
//
// ============================================================================
// **والنتيجةُ حتميّةٌ تُشتقّ من المبلغ**، وهذا قرارُ تصميمٍ لا اختصار: عرضٌ توضيحيّ يجب أن يُظهر
// الرفضَ والانتظارَ والإلغاءَ وإعادةَ المحاولة، لا النجاحَ وحده — ومولّدٌ عشوائيّ يجعل العرضَ
// غيرَ قابلٍ للتكرار واختبارَه غيرَ قابلٍ للكتابة. فالوحداتُ الصغرى الأخيرة من المبلغ تختار
// المسار، كما تفعل «المبالغ السحرية» في أوضاع اختبار المزوّدين الحقيقيين:
//
//   • …01 ⇒ **مرفوضة وقابلة لإعادة المحاولة** — الطلبُ يبقى معلّقاً ويعيد المشتري الكرّة.
//   • …02 ⇒ **قيد المعالجة** — لا تُؤكَّد ولا تُلغى الآن.
//   • …03 ⇒ **ملغاة لدى البوّابة** — الطلبُ يُلغى ويُحرَّر حجزه.
//   • ما عدا ذلك ⇒ **ناجحة**.
//
// والنتيجةُ تُختم في معرّف النيّة عند إنشائها، فيُقرأ منها عند التأكيد: **دالّةٌ نقيّة بلا حالة
// مخزَّنة**، فهي متماثلةُ الاستدعاء بالبناء — تأكيدان لنيّةٍ واحدة يعطيان الجواب نفسه دائماً.
//
// والاستردادُ يُسجَّل في دفترٍ بمفتاح عدم التكرار: إعادةُ المفتاح نفسه تعيد الاسترداد نفسه، كما
// يفعل مزوّدٌ حقيقي — فتُختبر عدمُ الازدواجية فعلاً لا ادّعاءً.
//
// والإشعاراتُ تُقبل موقَّعةً بـ HMAC-SHA256 بسرّ `Payments:Demo:WebhookSecret` إن ضُبط.
// ============================================================================
public sealed class DemoPaymentLedger
{
    private readonly ConcurrentDictionary<string, string> _refunds = new();

    // `re_demo_` لا `re_fake_` — بقيّةُ تسميةٍ سبقت ADR-0063؛ و`D` بشُرَطها للسبب نفسه في
    // `CreateIntentAsync` أدناه: ألّا يحمل معرّفٌ سلسلةَ أرقامٍ تشبه بطاقة.
    public string Refund(string idempotencyKey) => _refunds.GetOrAdd(idempotencyKey, _ => $"re_demo_{Guid.NewGuid():D}");

    public int RefundsFor(string idempotencyKeyPrefix) => _refunds.Keys.Count(k => k.StartsWith(idempotencyKeyPrefix, StringComparison.Ordinal));
}

public sealed class DemoPaymentGateway : IPaymentGateway
{
    // الاسمُ المسجَّل على الدفعة. صفوفٌ كُتبت حين كان اسمه "fake" تبقى صحيحة: الموجّه يوجّه كلَّ
    // ما ليس حساب متجرٍ إلى حساب النشر، فلا هجرةَ ولا قيمةٌ يتيمة.
    public const string GatewayName = "demo";
    public const string SignaturePrefix = "sha256=";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DemoPaymentLedger _ledger;
    private readonly string? _webhookSecret;

    public DemoPaymentGateway(DemoPaymentLedger ledger, string? webhookSecret)
    {
        _ledger = ledger;
        _webhookSecret = string.IsNullOrWhiteSpace(webhookSecret) ? null : webhookSecret;
    }

    public string Name => GatewayName;

    // لا مفتاح علني: الواجهة تعرض زرّ إتمام مباشر بدل نموذج البطاقة.
    public string? PublishableKey => null;

    public bool CanVerifyWebhooks => _webhookSecret is not null;

    // البوّابةُ التجريبية تقبل كلَّ شيء — وهو صحيحٌ عنها: لا مال ولا شبكة، فلا قيد.
    // البدءُ يُبنى على إنشاء النيّة نفسه: مسارُ شبكةٍ واحد لا اثنان، فما يُختبر اليوم هو ما
    // يُنفَّذ غداً. ومَن يُدخل مزوّداً يُعيد التوجيه يكتب هنا `Redirect` ولا يمسّ شيئاً فوقه.
    public async Task<StartPaymentResult> StartPaymentAsync(
        Money amount, string orderReference, int tenantId, CancellationToken ct)
    {
        var intent = await CreateIntentAsync(amount, orderReference, tenantId, ct);
        return new StartPaymentResult.ClientScript(intent.PaymentIntentId, intent.ClientSecret, PublishableKey);
    }

    public PaymentCapabilities Capabilities { get; } = new(
        AuthorizeThenCapture: true, PartialCapture: true, PartialRefund: true, StoredInstruments: true);

    // ========================================================================
    // النتيجةُ تُختار من المبلغ وتُختم في المعرّف.
    //
    // الختمُ هو ما يجعل `ConfirmAsync` **دالّةً نقيّة**: لا دفترَ نتائجَ يُستشار، ولا حالةَ
    // تُخزَّن، فتأكيدان لنيّةٍ واحدة يعطيان الجواب نفسه بالضرورة — وهو ما يعنيه أن يكون التأكيد
    // متماثلَ الاستدعاء، وهو ما يعتمد عليه سباقُ «العميل ضدّ الإشعار» في `OrderPaymentConfirmation`.
    // ========================================================================
    internal const string DeclinedCode = "declined";
    internal const string ProcessingCode = "processing";
    internal const string CancelledCode = "cancelled";
    internal const string SucceededCode = "ok";

    // آخرُ وحدتين صغريين تختاران المسار — **بخانات العملة نفسها** لا بخانتين مفترضتين: الدينار
    // ثلاثيُّ الخانات، فـ10.001 تنتهي بـ«01» فيه كما تنتهي 10.01 بها في عملةٍ ثنائية. حسابُها
    // بضربٍ ثابت في 100 كان يُنتج «01» لمبلغٍ لا ينتهي بها، وهو خطأٌ أمسكه اختبارُه أوّلَ مرّة.
    internal static string OutcomeFor(Money amount)
    {
        var factor = (decimal)Math.Pow(10, CurrencyInfo.MinorUnits(amount.Currency));
        return ((int)(Math.Abs(amount.Amount) * factor) % 100) switch
    {
            1 => DeclinedCode,
            2 => ProcessingCode,
            3 => CancelledCode,
            _ => SucceededCode,
        };
    }

    private static string OutcomeOf(string paymentIntentId)
    {
        var parts = paymentIntentId.Split('_');
        return parts.Length >= 3 ? parts[2] : SucceededCode;
    }

    // المعرّف بصيغة `D` (بشُرَطها) لا `N`، وهذا ليس ذوقاً: معرّفٌ من 32 خانة ستّ عشرية يحوي
    // أحياناً سلسلةَ أرقامٍ متّصلة بطول 13–19 — أي ما يشبه رقم بطاقة لأيّ ماسحٍ يبحث بنمط. وقع
    // فعلاً: `…4595870559329812…` أسقط الاختبار على CI بعد أن مرّ محلّياً، لأنّ الاحتمال ~1٪.
    // والشُّرَط تقطع السلسلة عند 12 خانة على الأكثر، فتصير الخاصّية صحيحةً **بالبناء** لا بالحظّ.
    public Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, int tenantId, CancellationToken ct)
    {
        var id = $"pi_demo_{OutcomeFor(amount)}_{Guid.NewGuid():D}";
        return Task.FromResult(new PaymentIntentResult(id, $"{id}_secret", Name, PublishableKey));
    }

    public Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct) =>
        Task.FromResult(OutcomeOf(paymentIntentId) switch
        {
            // مرفوضةٌ **وحيّة**: هذا ما يجعل إعادة المحاولة قابلةً للعرض — الطلب يبقى معلّقاً
            // على السرّ نفسه بدل أن يُلغى (ADR-0036).
            DeclinedCode => new PaymentConfirmationResult(PaymentIntentState.Retryable, "بطاقة مرفوضة (عرض توضيحي)"),
            ProcessingCode => new PaymentConfirmationResult(PaymentIntentState.Processing),
            CancelledCode => new PaymentConfirmationResult(PaymentIntentState.Cancelled, "أُلغيت لدى البوّابة (عرض توضيحي)"),
            _ => PaymentConfirmationResult.Ok(),
        });

    // ========================================================================
    // الإلغاءُ يُطاع دائماً، وهذا صحيحٌ عن **هذه** البوّابة تحديداً: لا جلسةَ لدى مزوّدٍ تُسأل،
    // و«الدفع» هنا ضغطةُ زرٍّ في المتصفّح — فنيّةٌ لم تُؤكَّد بعدُ لا شيء فيها يُقبض.
    //
    // ومزوّدٌ حقيقيّ **لا يجوز** أن يفعل هذا: عنده تُسأل الجلسةُ فقد تكون نجحت بين الطلب
    // والإلغاء، وادّعاءُ الإلغاء حينها يترك مالاً مقبوضاً على طلبٍ ملغى (ADR-0036). ولهذا
    // يعيد العقدُ **حالةً** لا `void`: المحوّلُ يقول ما وجد، والمنسّقُ يقرّر.
    // ========================================================================
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

        var body = JsonSerializer.Deserialize<DemoWebhookBody>(payload, Json);
        return body is { Type: "payment_intent.succeeded" or "payment_intent.payment_failed", OrderReference: { Length: > 0 } reference }
            ? new GatewayWebhookEvent(body.PaymentIntentId ?? "", reference, body.TenantId)
            : null;
    }

    public static string Sign(string payload, string secret) =>
        SignaturePrefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

    private sealed record DemoWebhookBody(string? Type, string? PaymentIntentId, string? OrderReference, int? TenantId);
}
