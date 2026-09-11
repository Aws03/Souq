using Souq.Domain.ValueObjects;

namespace Souq.Application.Common.Interfaces;

// ============================================================================
// IPaymentService — العقد مع بوّابة الدفع. نُعرّفه هنا (في Application) لكن
// ننفّذه في Infrastructure (StripePaymentService/FakePaymentService).
//
// الشكل بخطوتين (نيّة ثم تأكيد) لا شحن مباشر واحد: هذا هو النمط الصحيح لدفع
// حقيقي عبر Stripe.js — تفاصيل البطاقة لا تصل خادمنا إطلاقاً (تبقى بين متصفّح
// العميل وStripe مباشرة عبر Stripe Elements)، فيتحصّل الخادم فقط على نيّة دفع
// (PaymentIntent) يُصادق عليها العميل بمتصفحه، ثم يتحقّق الخادم من نتيجتها هنا.
// ============================================================================
public interface IPaymentService
{
    // ينشئ نيّة دفع بمبلغ محدَّد ويعيد سرّاً (ClientSecret) تستخدمه الواجهة مع
    // Stripe.js لإتمام الدفع مباشرة من متصفّح العميل.
    Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, CancellationToken ct = default);

    // يتحقّق من حالة نيّة دفع لدى بوّابة الدفع نفسها (لا نثق بادّعاء العميل وحده).
    Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct = default);

    // يلغي نيّة دفع لم تكتمل (طلب انتهت مهلته — المرحلة 6) ويعيد حالتها الحقيقية: ملغاة، أو نجحت قبل الإلغاء (سباق
    // مع دفع العميل ⇒ يُؤكَّد الطلب بدل إلغائه)، أو قيد المعالجة (لا تُلغى الآن — تُعاد المحاولة لاحقاً).
    Task<PaymentIntentState> CancelIntentAsync(string paymentIntentId, CancellationToken ct = default);

    // الإعدادات العامة التي تحتاجها الواجهة لتهيئة مزوّد الدفع (مفتاح Stripe.js العلني)
    // — لا أسرار هنا. null ⇒ لا مزوّد حقيقي مضبوط (البوّابة التجريبية).
    PaymentClientConfig GetClientConfig();

    // يتحقّق من توقيع إشعار البوّابة (Webhook) ويستخرج مرجع الطلب إن كان الحدث عن دفعة.
    // null ⇒ حدث لا يعنينا أو لا Webhook مضبوط. توقيع غير صالح ⇒ InvalidPaymentWebhookException.
    // التحقّق هنا لا في الـ Controller: صيغة التوقيع تفصيل خاص بكل مزوّد (Phase 0 D1).
    PaymentWebhookEvent? ParseWebhook(string payload, string? signatureHeader);
}

public record PaymentIntentResult(string PaymentIntentId, string ClientSecret);
public record PaymentConfirmationResult(bool Succeeded, string? FailureReason);
public record PaymentClientConfig(string? PublishableKey);
public record PaymentWebhookEvent(string OrderReference);
public enum PaymentIntentState { Cancelled, Succeeded, Processing }
