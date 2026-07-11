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
}

public record PaymentIntentResult(string PaymentIntentId, string ClientSecret);
public record PaymentConfirmationResult(bool Succeeded, string? FailureReason);
