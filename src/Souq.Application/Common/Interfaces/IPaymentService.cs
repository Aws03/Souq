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

    // يبدأ الدفع ويعيد **كيف** يُكمله الشاري (ADR-0048 §1). يحلّ محلّ `CreateIntentAsync` بالنسبة
    // لكلّ مُنادٍ جديد: ذاك يفترض شكلاً واحداً، وهذا ينقل الشكلَ كما يقوله المحوّل.
    Task<StartPaymentAttempt> StartPaymentAsync(Money amount, string orderReference, CancellationToken ct = default);

    // ما يستطيعه المزوّد المربوط — بياناتٌ يعلنها، لا استثناءاتٌ يُكتشف بها (ADR-0048 §5).
    Task<PaymentCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);

    // الإعدادات العامة التي تحتاجها الواجهة لتهيئة مزوّد الدفع (المفتاح العلني لحساب المتجر أو النشر) — لا أسرار هنا.
    // null ⇒ البوّابة التجريبية.
    Task<PaymentClientConfig> GetClientConfigAsync(CancellationToken ct = default);

    // يتحقّق من توقيع إشعار البوّابة (Webhook) ويستخرج مرجع الطلب ومتجره إن كان الحدث عن دفعة. null ⇒ حدث لا يعنينا أو
    // لا سرّ إشعارات مضبوط. توقيع غير صالح ⇒ InvalidPaymentWebhookException. صيغة التوقيع تفصيل المزوّد (Phase 0 D1).
    Task<PaymentWebhookEvent?> ParseWebhookAsync(string payload, string? signatureHeader, CancellationToken ct = default);
}

// Gateway: الحساب الذي أنشأ النيّة (يُسجَّل على الدفعة ولا يفسّره Application).
// GatewayAccount: هويّةُ الحساب الذي قبض — المفتاح العلني (TD-50، ADR-0061). null للبوّابة
// التجريبية التي لا مفتاح علنيّ لها، ولكلّ دفعةٍ كُتبت قبل هذا الحقل.
public record PaymentIntentResult(
    string PaymentIntentId, string ClientSecret, string Gateway = "deployment", string? GatewayAccount = null);

// نتيجة سؤال البوّابة عن نيّة دفع. State هي الحقيقة التي يُبنى عليها القرار؛ Succeeded اختصار قراءة.
public record PaymentConfirmationResult(PaymentIntentState State, string? FailureReason = null)
{
    public bool Succeeded => State == PaymentIntentState.Succeeded;

    public static PaymentConfirmationResult Ok() => new(PaymentIntentState.Succeeded);
}
public record PaymentRefundResult(bool Succeeded, string? ProviderRefundId, string? FailureReason);

// ============================================================================
// **كيف يبدأ الدفع** — نتيجةٌ مُميَّزة تحملها النواة بلا أن تفهمها (ADR-0048 §1).
//
// اليومَ الشكلُ واحد: سرٌّ يُسلَّم للمتصفّح فيُكمل الدفع في الصفحة (`ClientScript`). وهذا شكلُ
// المزوّد الحاليّ وحده — **والمزوّدون الإقليميون الذين يخدمون هذا السوق يعيدون توجيهاً**، أي
// أنّ الشاري يغادر الصفحة ويعود. وحين كان العقدُ يقول «أعطني سرّاً» كان إدخالُ مزوّدٍ كهذا
// **يغيّر واجهةً في Application** — وهو بالضبط عكسُ ما يَعِد به المنفذ.
//
// فالشكلُ صار نتيجةً مُميَّزة: المحوّلُ يقول كيف يبدأ، والنواةُ تنقله إلى الواجهة كما هو.
// إضافةُ مزوّدٍ يُعيد التوجيه صارت **تغييرَ محوّلٍ وحده**.
//
//   • `ClientScript`  — سرٌّ وإعدادٌ للواجهة؛ الشاري لا يغادر (الشكلُ القائم).
//   • `Redirect`      — رابطٌ يُذهب إليه الشاري ويعود منه.
//   • `BrowserPost`   — نموذجٌ يُرسَل بحقولٍ موقَّعة إلى صفحة المزوّد.
//   • `Completed`     — حُسم فوراً بلا خطوةٍ في المتصفّح.
//   • `Deferred`      — تعليماتٌ تُنفَّذ خارج النطاق: الدفعُ عند الاستلام وفواتيرُ السداد، وهما
//                       في هذا السوق ليسا حالاتٍ هامشية.
//
// **ولا تُبنى هنا شاشةٌ لشكلٍ لا يُنتجه محوّل**: الأشكالُ الأربعة الأخرى معرَّفةٌ ليكون العقدُ
// صحيحاً، ومَن يُدخل أوّلَ مزوّدٍ يُعيد التوجيه يبني شاشته معه. تعريفُ الشكل ليس تعميماً
// استباقياً — هو موضعُ الفصل؛ أمّا بناءُ واجهةٍ لمزوّدٍ لا وجود له فهو كذلك، ولا يُبنى.
// ============================================================================
// ما تحتاجه النواة من البدء: الشكلُ، والحسابُ الذي قبض ونوعُه — الأخيران يُسجَّلان على الدفعة.
public sealed record StartPaymentAttempt(StartPaymentResult Result, string Gateway, string? GatewayAccount);

public abstract record StartPaymentResult(string ProviderReference)
{
    // سرٌّ للمتصفّح: الشاري يُكمل الدفع في الصفحة نفسها.
    public sealed record ClientScript(string Reference, string ClientSecret, string? PublishableKey)
        : StartPaymentResult(Reference);

    // رابطٌ يغادر إليه الشاري ويعود. **ولهذا وُجد سجلُّ المحاولة في ADR-0048**: العودةُ والإشعارُ
    // والمُطابِقُ قد تصل بأيّ ترتيب — ويُبنى مع أوّل محوّلٍ يُنتج هذا الشكل، لا قبله.
    public sealed record Redirect(string Reference, string Url) : StartPaymentResult(Reference);

    public sealed record BrowserPost(string Reference, string Url, IReadOnlyDictionary<string, string> Fields)
        : StartPaymentResult(Reference);

    public sealed record Completed(string Reference, PaymentIntentState State) : StartPaymentResult(Reference);

    public sealed record Deferred(string Reference, string Instructions) : StartPaymentResult(Reference);
}

// ============================================================================
// قدراتُ المزوّد — **بياناتٌ يعلنها، لا استثناءاتٌ تُكتشف** (ADR-0048 §5).
//
// النواةُ تسأل قبل أن تعرض، فلا تَعِد التاجرَ بما لا يُنفَّذ ثمّ تُخفق عند النداء. وهذا ليس
// تعميماً استباقياً: قاعدتان منه تُنفَّذان اليوم ولهما ضحيّةٌ حقيقية — استردادٌ جزئيٌّ لدى مزوّدٍ
// لا يدعمه، ومبلغٌ لا يقبله المزوّد بدقّته.
//
// **AmountGranularityMinorUnits قيدُ تسعيرٍ لا تنسيق.** بعضُ المزوّدين يوثّق أنّ شبكةَ بطاقاتٍ
// تشترط أن ينتهي المبلغ بصفر في عملةٍ ثلاثية الخانات — أي أنّ أصغر زيادةٍ ممكنة عشرُ وحداتٍ
// صغرى لا واحدة، وهو ما يمتدّ إلى أسعار المنتجات نفسها. **ولا قيمةَ لأيّ سوقٍ مكتوبةٌ هنا**:
// المحوّلُ يعلن ما يوثّقه مزوّدُه، والافتراضُ `1` يعني «بلا قيد» — فقاعدةٌ لم يتحقّق منها أحدٌ
// لا تدخل المنتج بحجّة الاحتياط.
//
// SupportedCurrencies فارغةٌ تعني «بلا قيدٍ معلن»، لا «لا شيء».
// ============================================================================
public sealed record PaymentCapabilities(
    bool AuthorizeThenCapture = false,
    bool PartialCapture = false,
    bool PartialRefund = true,
    bool StoredInstruments = false,
    bool TransactionTimeSplit = false,
    bool ProgrammaticOnboarding = false,
    IReadOnlySet<string>? SupportedCurrencies = null,
    int AmountGranularityMinorUnits = 1)
{
    public bool Supports(string currency) =>
        SupportedCurrencies is not { Count: > 0 } allowed || allowed.Contains(currency);
}
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
