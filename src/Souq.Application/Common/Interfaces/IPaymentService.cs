using Souq.Domain.ValueObjects;

namespace Souq.Application.Common.Interfaces;

// ============================================================================
// IPaymentService — العقد مع بوّابة الدفع. نُعرّفه هنا (في Application) ويُنفَّذ في Infrastructure بموجّه واحد يختار
// الحساب لكل استدعاء: حساب المتجر إن رُبط، وإلا حساب النشر (المرحلة 11، ADR-0031). تبديل المزوّد لا يمسّ Application.
//
// الشكل بخطوتين (نيّة ثم تأكيد) لا شحن مباشر واحد: تفاصيل البطاقة لا تصل خادمنا إطلاقاً (تبقى بين متصفّح العميل والبوّابة
// عبر Stripe Elements)، فيحصل الخادم على نيّة دفع يُصادق عليها العميل بمتصفحه، ثم يتحقّق من نتيجتها هنا.
// ============================================================================
public interface IPaymentService
{
    // ينشئ نيّة دفع بمبلغ محدَّد ويعيد سرّاً (ClientSecret) تستخدمه الواجهة لإتمام الدفع من المتصفّح، والحساب الذي أنشأها.
    Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, CancellationToken ct = default);

    // يتحقّق من حالة نيّة دفع لدى بوّابة الدفع نفسها (لا نثق بادّعاء العميل وحده). النتيجة تحمل الحالة لا مجرّد
    // "نجح/لم ينجح": ما يُفعَل بالطلب يختلف جذرياً بين نيّة أُلغيت ونيّة ما زالت تقبل محاولة أخرى (R-02).
    Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct = default);

    // يلغي نيّة دفع لم تكتمل (طلب انتهت مهلته — المرحلة 6) ويعيد حالتها الحقيقية: ملغاة، أو نجحت قبل الإلغاء (سباق
    // مع دفع العميل ⇒ يُؤكَّد الطلب بدل إلغائه)، أو قيد المعالجة (لا تُلغى الآن — تُعاد المحاولة لاحقاً).
    Task<PaymentIntentState> CancelIntentAsync(string paymentIntentId, CancellationToken ct = default);

    // استرداد جزئي أو كامل (المرحلة 11) بمفتاح عدم تكرار: إعادة الطلب بالمفتاح نفسه لا تردّ المال مرتين. رفض البوّابة
    // نتيجة (Succeeded = false)؛ انقطاعها استثناء — النتيجة مجهولة ويُعاد بالمفتاح نفسه.
    Task<PaymentRefundResult> RefundAsync(string paymentIntentId, Money amount, string idempotencyKey, CancellationToken ct = default);

    // الإعدادات العامة التي تحتاجها الواجهة لتهيئة مزوّد الدفع (المفتاح العلني لحساب المتجر أو النشر) — لا أسرار هنا.
    // null ⇒ البوّابة التجريبية.
    Task<PaymentClientConfig> GetClientConfigAsync(CancellationToken ct = default);

    // يتحقّق من توقيع إشعار البوّابة (Webhook) ويستخرج مرجع الطلب ومتجره إن كان الحدث عن دفعة. null ⇒ حدث لا يعنينا أو
    // لا سرّ إشعارات مضبوط. توقيع غير صالح ⇒ InvalidPaymentWebhookException. صيغة التوقيع تفصيل المزوّد (Phase 0 D1).
    Task<PaymentWebhookEvent?> ParseWebhookAsync(string payload, string? signatureHeader, CancellationToken ct = default);
}

// Gateway: الحساب الذي أنشأ النيّة (يُسجَّل على الدفعة ولا يفسّره Application).
public record PaymentIntentResult(string PaymentIntentId, string ClientSecret, string Gateway = "deployment");

// نتيجة سؤال البوّابة عن نيّة دفع. State هي الحقيقة التي يُبنى عليها القرار؛ Succeeded اختصار قراءة.
public record PaymentConfirmationResult(PaymentIntentState State, string? FailureReason = null)
{
    public bool Succeeded => State == PaymentIntentState.Succeeded;

    public static PaymentConfirmationResult Ok() => new(PaymentIntentState.Succeeded);
}
public record PaymentRefundResult(bool Succeeded, string? ProviderRefundId, string? FailureReason);
public record PaymentClientConfig(string? PublishableKey);

// TenantId: المتجر الذي أنشأ النيّة (null لنيّات ما قبل المرحلة 11). VerifiedByStoreAccount: وقّعه سرّ حساب المتجر نفسه،
// فلا يُوجَّه لمتجر غيره.
public record PaymentWebhookEvent(
    string OrderReference, string? PaymentIntentId = null, int? TenantId = null, bool VerifiedByStoreAccount = false);

// حالة نيّة الدفع لدى البوّابة:
//   Cancelled  — نهائية: لا تقبض بعد الآن، فيُؤمَن إلغاء طلبها.
//   Succeeded  — نهائية: قُبض المال.
//   Processing — لم تُحسم بعد؛ لا تُلغى ولا تُؤكَّد الآن.
//   Retryable  — حيّة وتقبل محاولة أخرى على السرّ نفسه (بطاقة مرفوضة تُعيد نيّة Stripe إلى requires_payment_method).
//                إلغاء طلبها مع تركها حيّة هو بالضبط ما يسمح بقبض مال على طلب ملغى (R-02).
// CancelIntentAsync لا تعيد Retryable أبداً: هي تُلغي ما كان كذلك.
public enum PaymentIntentState { Cancelled, Succeeded, Processing, Retryable }
