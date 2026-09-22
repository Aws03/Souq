using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Payments;

// ============================================================================
// محوّل بوّابة بحساب واحد (مفاتيحه معطاة له) — لا يعرف المتاجر ولا القاعدة (المرحلة 11، ADR-0031). الموجّه
// (PaymentGatewayRouter) يختار لكل استدعاء أيّ حساب يُستخدم، وهو وحده ما يراه Application (IPaymentService):
// تبديل البوّابة أو إضافة مزوّد = محوّل جديد هنا، بلا تغيير في Application.
// ============================================================================
// ============================================================================
// مصنعُ بوّابةِ حسابِ المتجر (TD-52).
//
// كان `PaymentGatewayRouter` يُنشئ `StripeGateway` بنفسه بسطرين — فكان **فرعُ حساب المتجر غير
// قابلٍ للاختبار خارج الشبكة**: أيُّ فحصٍ لقواعد التوجيه كان سيكلّم Stripe فعلاً، حتى في بيئة
// الاختبار. وكلُّ قاعدةٍ يعتمد عليها جوابُ المالك `D-13` — حسابُ المتجر إن رُبط، وحسابُ النشر
// وإلّا، ونوعُ الحساب يُسجَّل على الدفعة لما بعدها، و**503 بدل رجوعٍ صامت** حين يتعذّر فكُّ
// السرّ — كانت ترتكز على قراءة الشيفرة وحدها.
//
// الحقنُ يغيّر ذلك ولا يغيّر سلوكاً: التسجيلُ الافتراضي هو السطران نفساهما.
// ============================================================================
public delegate IPaymentGateway StoreGatewayFactory(string accountKind, StripeCredentials credentials);

public interface IPaymentGateway
{
    // يُسجَّل على الدفعة ("stripe:store"، "stripe:deployment"، "fake") — ليمرّ ما بعد الإنشاء بالحساب نفسه.
    string Name { get; }

    string? PublishableKey { get; }

    bool CanVerifyWebhooks { get; }

    Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, int tenantId, CancellationToken ct);

    Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct);

    Task<PaymentIntentState> CancelIntentAsync(string paymentIntentId, CancellationToken ct);

    Task<PaymentRefundResult> RefundAsync(string paymentIntentId, Money amount, string idempotencyKey, CancellationToken ct);

    // null ⇒ حدث لا يخصّ دفعة طلب، أو لا سرّ إشعارات لهذا الحساب. توقيع غير صالح ⇒ InvalidPaymentWebhookException.
    GatewayWebhookEvent? ParseWebhook(string payload, string? signatureHeader);
}

// TenantId من بيانات النيّة الوصفية: المتجر الذي أنشأها (null لنيّات ما قبل المرحلة 11).
public sealed record GatewayWebhookEvent(string PaymentIntentId, string OrderReference, int? TenantId);

// حساب النشر الافتراضي (Stripe بمفاتيح الإعداد، أو التجريبية في التطوير/الاختبار) — Singleton يختاره DI عند الإقلاع.
public sealed class DeploymentPaymentGateway
{
    public DeploymentPaymentGateway(IPaymentGateway gateway) => Gateway = gateway;
    public IPaymentGateway Gateway { get; }
}
