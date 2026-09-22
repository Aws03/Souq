using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Payments;

namespace Souq.IntegrationTests;

// ============================================================================
// قواعدُ توجيه الدفع (TD-52) — **أربعُ قواعدَ يقوم عليها جوابُ المالك `D-13`، ولم يكن يفحصها شيء.**
//
// كان الموجّه يُنشئ `StripeGateway` بنفسه بسطرين، فأيُّ اختبارٍ لفرع حساب المتجر كان سيكلّم
// Stripe عبر الشبكة — حتى في بيئة الاختبار. فبقيت القواعدُ الأربع مُثبتةً بقراءة الشيفرة وحدها:
// راجعتها M6 ووجدتها صحيحة، وسجّلت الفجوة بدل أن تدّعي تغطية.
//
// والآن يُحقن المصنع، فتُفحص القواعد ببديلٍ بلا شبكة ولا قاعدة — ولذلك يعيش هذا الملفّ خارج
// `IntegrationCollection`: لا يحتاج SQL Server، ولا يجب أن ينتظر تهيئته.
//
//   1. حسابُ المتجر حين يكون مربوطاً،
//   2. وحسابُ النشر حين لا يكون،
//   3. ونوعُ الحساب المسجَّل على الدفعة هو ما يوجّه ما بعد الإنشاء،
//   4. و**503 لا رجوعٌ صامت** حين يتعذّر فكُّ سرّ المتجر — فمالُ متجرٍ لا يُقبض في حساب غيره.
// ============================================================================
public class PaymentGatewayRoutingTests
{
    private const int TenantId = 7;

    private sealed class RecordingGateway : IPaymentGateway
    {
        public RecordingGateway(string name) => Name = name;

        public string Name { get; }
        public string? PublishableKey => $"pk_{Name}";
        public bool CanVerifyWebhooks => false;

        public Task<PaymentIntentResult> CreateIntentAsync(Money amount, string orderReference, int tenantId, CancellationToken ct) =>
            Task.FromResult(new PaymentIntentResult($"pi_{Name}", "secret", PublishableKey!));

        public Task<PaymentConfirmationResult> ConfirmAsync(string paymentIntentId, CancellationToken ct) =>
            Task.FromResult(PaymentConfirmationResult.Ok());

        public Task<PaymentIntentState> CancelIntentAsync(string paymentIntentId, CancellationToken ct) =>
            Task.FromResult(PaymentIntentState.Cancelled);

        public Task<PaymentRefundResult> RefundAsync(string paymentIntentId, Money amount, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(new PaymentRefundResult(true, Name, null));

        public GatewayWebhookEvent? ParseWebhook(string payload, string? signatureHeader) => null;
    }

    private static readonly RecordingGateway Deployment = new(StripeGateway.DeploymentAccount);

    // ما سُجّل على الدفعة: النوعُ، والهويّةُ التي يصنعها `RecordingGateway` لذلك النوع — فالحالةُ
    // الطبيعية «الحساب نفسه»، والمخالفةُ تُكتب صراحةً في اختبارها.
    private static PaymentAccountRef Recorded(string kind, string? account = null) =>
        new(kind, account ?? $"pk_{kind}");

    private static StorePaymentAccount Account() =>
        new("pk_test_abcdefghij", "cipher", "hint", liveMode: false, webhookSecretCipher: null, updatedByUserId: null);

    // بدائلُ مكتوبةٌ بخطّ اليد لا مكتبةُ تزييف: هذا المشروعُ لا يحمل واحدة، وإضافةُ تبعيةٍ من
    // أجل ثلاث واجهاتٍ بدالّةٍ واحدة لكلٍّ منها ثمنٌ دائم مقابل راحةٍ عابرة.
    private sealed class FakeAccounts : IStorePaymentAccountRepository
    {
        private readonly StorePaymentAccount? _account;
        public FakeAccounts(StorePaymentAccount? account) => _account = account;

        public Task<StorePaymentAccount?> GetAsync(CancellationToken ct = default) => Task.FromResult(_account);

        // بقيّةُ العقد لا يستعملها الموجّه، فلا تُزيَّف بسلوكٍ يُقرأ كأنّه مقصود.
        public Task<StorePaymentAccount?> GetByIdAsync(int id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(StorePaymentAccount entity, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public void Remove(StorePaymentAccount entity) => throw new NotSupportedException();
    }

    private sealed class FakePayments : IPaymentRepository
    {
        private readonly PaymentAccountRef? _recorded;
        public FakePayments(PaymentAccountRef? recorded) => _recorded = recorded;

        public Task<PaymentAccountRef?> GetAccountAsync(string providerPaymentId, CancellationToken ct = default) =>
            Task.FromResult(_recorded);

        public Task<Payment?> GetForOrderAsync(int orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
        public Task<Payment?> GetByIdAsync(int id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(Payment entity, CancellationToken ct = default) => throw new NotSupportedException();
        public void Remove(Payment entity) => throw new NotSupportedException();
    }

    private sealed class FakeSecrets : ISecretProtector
    {
        private readonly bool _broken;
        public FakeSecrets(bool broken) => _broken = broken;

        public bool IsConfigured => true;
        public string Protect(string plaintext, string purpose) => plaintext;

        public string Unprotect(string ciphertext, string purpose) => _broken
            ? throw new SecretUnavailableException("مفتاح أُزيل بعد تدوير")
            : "sk_store";
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public TenantScope Scope => TenantScope.Tenant;

        public TenantInfo? Tenant { get; } = new(
            TenantId, "demo", "متجر", TenantStatus.Active, "JOD", "ar", "Asia/Amman",
            new HashSet<string>(), new Dictionary<string, int>());
    }

    private static PaymentGatewayRouter Router(
        StorePaymentAccount? account, PaymentAccountRef? recorded = null, bool secretsBroken = false)
    {
        // المصنعُ المحقون: هنا كان الاختبارُ مستحيلاً بلا شبكة.
        StoreGatewayFactory factory = (kind, _) => new RecordingGateway(kind);

        return new PaymentGatewayRouter(
            new DeploymentPaymentGateway(Deployment), new FakeAccounts(account), new FakePayments(recorded),
            new FakeSecrets(secretsBroken), new FakeTenantContext(),
            NullLogger<PaymentGatewayRouter>.Instance, factory);
    }

    [Fact]
    public async Task متجر_ربط_حسابه_يقبض_فيه()
    {
        var result = await Router(Account()).CreateIntentAsync(Money.FromCalculation(10m, "JOD"), "ORD-1");

        result.PaymentIntentId.Should().Be($"pi_{StripeGateway.StoreAccount}");
    }

    [Fact]
    public async Task متجر_بلا_حساب_يقبض_في_حساب_النشر()
    {
        var result = await Router(account: null).CreateIntentAsync(Money.FromCalculation(10m, "JOD"), "ORD-1");

        result.PaymentIntentId.Should().Be($"pi_{StripeGateway.DeploymentAccount}");
    }

    [Fact]
    public async Task إعداد_الواجهة_يتبع_الحساب_الذي_سيقبض()
    {
        (await Router(Account()).GetClientConfigAsync()).PublishableKey
            .Should().Be($"pk_{StripeGateway.StoreAccount}");

        (await Router(account: null).GetClientConfigAsync()).PublishableKey
            .Should().Be($"pk_{StripeGateway.DeploymentAccount}");
    }

    // ما بعد الإنشاء يتبع **الحساب المسجَّل على الدفعة** لا الحساب الحالي — وهو بالضبط الموضع
    // الذي يقف عنده TD-50: المسجَّلُ نوعٌ لا هويّة، فاستبدالُ المتجر مفاتيحَه يوجّه الاسترداد
    // إلى حسابٍ آخر من النوع نفسه.
    [Fact]
    public async Task الاسترداد_يتبع_نوع_الحساب_المسجَّل_على_الدفعة()
    {
        var router = Router(Account(), Recorded(StripeGateway.StoreAccount));

        var refund = await router.RefundAsync("pi_1", Money.FromCalculation(5m, "JOD"), "idem-1");

        refund.ProviderRefundId.Should().Be(StripeGateway.StoreAccount,
            "المرجعُ يحمل اسمَ الحساب الذي نفّذ — وهو ما يُثبت أيُّهما اختير");
    }

    [Fact]
    public async Task دفعة_أخذها_حساب_النشر_تُستردّ_منه_ولو_ربط_المتجر_حسابه_بعدها()
    {
        var router = Router(Account(), Recorded(StripeGateway.DeploymentAccount));

        var refund = await router.RefundAsync("pi_1", Money.FromCalculation(5m, "JOD"), "idem-1");

        refund.ProviderRefundId.Should().Be(StripeGateway.DeploymentAccount);
    }

    [Fact]
    public async Task دفعة_أخذها_حساب_المتجر_ثمّ_فُكّ_ربطه_تُرفض_برسالة_لا_تُوجَّه_لحساب_النشر()
    {
        var router = Router(account: null, Recorded(StripeGateway.StoreAccount));

        var act = () => router.RefundAsync("pi_1", Money.FromCalculation(5m, "JOD"), "idem-1");

        await act.Should().ThrowAsync<PaymentGatewayUnavailableException>();
    }

    // ========================================================================
    // TD-50 (ADR-0061): الهويّة تُقارَن، لا النوعُ وحده.
    //
    // المسارُ الواقعيّ: متجرٌ يربط مفاتيح تجريبية، يقبض دفعة، ثمّ يبدّل إلى مفاتيح حقيقية —
    // وهو مسارٌ يدعمه الكيان صراحةً. النوعُ يبقى `stripe:store` في الحالتين، فكان الاسترداد
    // يُرسَل إلى حسابٍ لا يعرف تلك النيّة، ويُعلَّم Failed، **ولا يُعاد استرداد فاشل**.
    // ========================================================================
    [Fact]
    public async Task استرداد_دفعة_أخذها_حسابٌ_آخر_يُرفض_قبل_النداء_لا_يفشل_بعده()
    {
        // سُجّل على الدفعة حسابٌ بمفتاحٍ علنيّ آخر — أي مفاتيحُ المتجر بُدّلت بعد القبض.
        var router = Router(Account(), Recorded(StripeGateway.StoreAccount, "pk_test_theoldone"));

        var act = () => router.RefundAsync("pi_1", Money.FromCalculation(5m, "JOD"), "idem-1");

        await act.Should().ThrowAsync<PaymentGatewayUnavailableException>();
    }

    [Fact]
    public async Task الهويّة_المطابقة_تمرّ()
    {
        var router = Router(Account(), Recorded(StripeGateway.StoreAccount));

        var refund = await router.RefundAsync("pi_1", Money.FromCalculation(5m, "JOD"), "idem-1");

        refund.Succeeded.Should().BeTrue();
    }

    // دفعةٌ كُتبت قبل هذا الحقل، أو أخذتها البوّابة التجريبية بلا مفتاح علنيّ: تبقى على سلوكها
    // السابق. حارسٌ يرفض المجهول كان سيمنع استرداد كلّ دفعةٍ قائمة في كلّ متجر يوم الترقية.
    [Fact]
    public async Task دفعة_بلا_هويّة_مسجَّلة_تمرّ_كما_كانت()
    {
        var router = Router(Account(), new PaymentAccountRef(StripeGateway.StoreAccount, null));

        var refund = await router.RefundAsync("pi_1", Money.FromCalculation(5m, "JOD"), "idem-1");

        refund.Succeeded.Should().BeTrue();
    }

    // **القاعدة الرابعة، وهي التي يقع عليها ضررٌ ماليّ حقيقيّ لو انعكست**: سرٌّ لا يُفكّ يوقف
    // الدفع، ولا يرجع صامتاً إلى حساب النشر — فيُقبض مالُ متجرٍ في حساب غيره.
    [Fact]
    public async Task سرّ_متجر_لا_يُفكّ_يوقف_الدفع_ولا_يرجع_صامتاً_لحساب_النشر()
    {
        var router = Router(Account(), secretsBroken: true);

        var act = () => router.CreateIntentAsync(Money.FromCalculation(10m, "JOD"), "ORD-1");

        await act.Should().ThrowAsync<PaymentGatewayUnavailableException>();
    }
}
