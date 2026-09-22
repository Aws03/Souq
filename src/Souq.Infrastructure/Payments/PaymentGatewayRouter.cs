using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Payments;

// ============================================================================
// PaymentGatewayRouter — التنفيذ الوحيد لـ IPaymentService (المرحلة 11، ADR-0031، D-13): لكل استدعاء يختار الحساب.
//   • إنشاء نيّة وإعداد الواجهة: حساب المتجر إن رُبط، وإلا حساب النشر.
//   • تأكيد/إلغاء/استرداد نيّة قائمة: **نوع** الحساب المسجَّل على دفعتها — أي حساب المتجر الحالي إن كان النوع
//     "stripe:store". الدفعة تحفظ النوع لا الهويّة، فاستبدال المتجر حسابه بين القبض والاسترداد يوجّه الاسترداد
//     إلى الحساب الجديد (⚠️ TD-50). صُحِّح هذا الوصف في M6 بعد أن كان يقول "الحساب الذي أنشأها" بإطلاق.
//   • الإشعار: سرّ حساب المتجر أولاً إن ضُبط، ثم سرّ حساب النشر — والنتيجة تذكر أيّهما وقّعه، فحدث وقّعه حساب متجر لا
//     يُوجَّه لمتجر غيره.
// سرّ متجر لا يُفكّ ⇒ PaymentGatewayUnavailableException (503) — لا رجوع صامت لحساب النشر يقبض مال المتجر في حساب غيره.
// ============================================================================
public sealed class PaymentGatewayRouter : IPaymentService
{
    private readonly DeploymentPaymentGateway _deployment;
    private readonly IStorePaymentAccountRepository _accounts;
    private readonly IPaymentRepository _payments;
    private readonly ISecretProtector _secrets;
    private readonly ITenantContext _tenant;
    private readonly ILogger<PaymentGatewayRouter> _logger;
    private readonly StoreGatewayFactory _storeGateway;

    private bool _storeResolved;
    private IPaymentGateway? _store;

    public PaymentGatewayRouter(
        DeploymentPaymentGateway deployment, IStorePaymentAccountRepository accounts, IPaymentRepository payments,
        ISecretProtector secrets, ITenantContext tenant, ILogger<PaymentGatewayRouter> logger,
        StoreGatewayFactory storeGateway)
    {
        _deployment = deployment; _accounts = accounts; _payments = payments; _secrets = secrets; _tenant = tenant;
        _logger = logger; _storeGateway = storeGateway;
    }

    public async Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, CancellationToken ct = default) =>
        await (await CurrentAsync(ct)).CreateIntentAsync(amount, orderReference, _tenant.RequireTenant().Id, ct);

    // البدءُ يمرّ بالحساب الذي سيقبض، ويعيد معه نوعَه وهويّته — فما يُسجَّل على الدفعة يأتي من
    // موضعٍ واحد لا من تخمينِ المُنادي.
    public async Task<StartPaymentAttempt> StartPaymentAsync(
        Money amount, string orderReference, CancellationToken ct = default)
    {
        var gateway = await CurrentAsync(ct);
        var result = await gateway.StartPaymentAsync(amount, orderReference, _tenant.RequireTenant().Id, ct);
        return new StartPaymentAttempt(result, gateway.Name, gateway.PublishableKey);
    }

    public async Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct = default) =>
        await (await ForIntentAsync(paymentIntentId, ct)).ConfirmAsync(paymentIntentId, ct);

    public async Task<PaymentIntentState> CancelIntentAsync(string paymentIntentId, CancellationToken ct = default) =>
        await (await ForIntentAsync(paymentIntentId, ct)).CancelIntentAsync(paymentIntentId, ct);

    public async Task<PaymentRefundResult> RefundAsync(
        string paymentIntentId, Money amount, string idempotencyKey, CancellationToken ct = default) =>
        await (await ForIntentAsync(paymentIntentId, ct)).RefundAsync(paymentIntentId, amount, idempotencyKey, ct);

    // قدراتُ الحساب الذي سيقبض — لا قدراتُ المزوّد بإطلاق: متجرٌ ربط حسابه قد يختلف عن حساب النشر.
    public async Task<PaymentCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
        (await CurrentAsync(ct)).Capabilities;

    public async Task<PaymentClientConfig> GetClientConfigAsync(CancellationToken ct = default) =>
        new((await CurrentAsync(ct)).PublishableKey);

    public async Task<PaymentWebhookEvent?> ParseWebhookAsync(string payload, string? signatureHeader, CancellationToken ct = default)
    {
        var deployment = _deployment.Gateway;
        if (await StoreAsync(ct) is { CanVerifyWebhooks: true } store)
        {
            try
            {
                return ToEvent(store.ParseWebhook(payload, signatureHeader), verifiedByStoreAccount: true);
            }
            catch (InvalidPaymentWebhookException) when (deployment.CanVerifyWebhooks)
            {
                // ليس توقيع حساب المتجر — قد يكون حدثاً من حساب النشر وصل على مضيف هذا المتجر.
            }
        }

        if (!deployment.CanVerifyWebhooks)
        {
            _logger.LogWarning("Payment webhook received but no webhook secret is configured for this store or the deployment; ignored");
            return null;
        }
        return ToEvent(deployment.ParseWebhook(payload, signatureHeader), verifiedByStoreAccount: false);
    }

    private static PaymentWebhookEvent? ToEvent(GatewayWebhookEvent? evt, bool verifiedByStoreAccount) =>
        evt is null ? null : new PaymentWebhookEvent(evt.OrderReference, evt.PaymentIntentId, evt.TenantId, verifiedByStoreAccount);

    private async Task<IPaymentGateway> CurrentAsync(CancellationToken ct) => await StoreAsync(ct) ?? _deployment.Gateway;

    // ========================================================================
    // الحساب الذي أنشأ النيّة. نيّة بلا دفعة مسجَّلة ⇒ الحساب الحالي.
    //
    // **والهويّة تُقارَن، لا النوعُ وحده** (TD-50، ADR-0061). متجرٌ ربط مفاتيح تجريبية، قبض
    // دفعة، ثمّ بدّل إلى مفاتيح حقيقية — مسارُ تشغيلٍ عاديّ يدعمه الكيان صراحةً — كان استردادُه
    // يُرسَل إلى الحساب الجديد الذي لا يعرف تلك النيّة، فيردّ المزوّد 404 ويُعلَّم الاسترداد
    // Failed، **ولا يُعاد استرداد فاشل**. فيبقى مالُ الزبون بلا طريقٍ داخل التطبيق.
    //
    // فالرفضُ قبل النداء أفضلُ من فشلٍ بعده: الأوّل حالةٌ يُصلحها المشغّل بإعادة ربط الحساب،
    // والثاني سجلٌّ ميّت. ولذلك الرسالةُ تقول ما يُفعل لا ما وقع.
    //
    // وهويّةٌ غير مسجَّلة (null) تمرّ: دفعاتٌ كُتبت قبل هذا الحقل، والبوّابةُ التجريبية بلا مفتاح
    // علنيّ. حارسٌ يرفض المجهول كان سيمنع استرداد كلّ دفعةٍ قائمة في كلّ متجر.
    // ========================================================================
    private async Task<IPaymentGateway> ForIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        var recorded = await _payments.GetAccountAsync(paymentIntentId, ct);
        if (recorded is null) return await CurrentAsync(ct);

        var gateway = recorded.Kind == StripeGateway.StoreAccount
            ? await StoreAsync(ct) ?? throw new PaymentGatewayUnavailableException(
                "هذه الدفعة أخذها حساب المتجر، وحساب المتجر لم يعد مربوطاً — أعد ربطه لإتمام العملية")
            : _deployment.Gateway;

        if (recorded.Account is { } account && gateway.PublishableKey is { } current
            && !string.Equals(account, current, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Payment {IntentId} was taken by account {Recorded} but the account in use now is {Current}; refusing",
                paymentIntentId, account, current);
            throw new PaymentGatewayUnavailableException(
                "هذه الدفعة أخذها حسابٌ آخر غير المربوط الآن، فلا تُنفَّذ عليه — أعد ربط الحساب الذي قبضها");
        }

        return gateway;
    }

    // حساب المتجر مفكوك السرّ، مرة واحدة لكل نطاق.
    private async Task<IPaymentGateway?> StoreAsync(CancellationToken ct)
    {
        if (_storeResolved) return _store;

        var account = await _accounts.GetAsync(ct);
        if (account is not null)
        {
            var tenantId = _tenant.RequireTenant().Id;
            try
            {
                var secret = _secrets.Unprotect(account.SecretKeyCipher, SecretPurposes.StripeSecretKey(tenantId));
                var webhook = account.WebhookSecretCipher is null
                    ? null
                    : _secrets.Unprotect(account.WebhookSecretCipher, SecretPurposes.StripeWebhookSecret(tenantId));
                _store = _storeGateway(StripeGateway.StoreAccount,
                    new StripeCredentials(secret, account.PublishableKey, webhook));
            }
            catch (SecretUnavailableException ex)
            {
                _logger.LogError(ex, "Payment account secrets of store {TenantId} can't be decrypted; its payments are unavailable", tenantId);
                throw new PaymentGatewayUnavailableException("حساب الدفع الخاص بالمتجر غير متاح الآن", ex);
            }
        }

        _storeResolved = true;
        return _store;
    }
}
