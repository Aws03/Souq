using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Payments;
using Souq.Infrastructure.Security;

namespace Souq.IntegrationTests;

// ============================================================================
// محوّلات المرحلة 11 بلا قاعدة ولا شبكة: تشفير أسرار المتاجر (AES-GCM بغرض مربوط بالمالك، تدوير المفاتيح، رفض التلاعب)،
// والبوّابة التجريبية (عدم تكرار الاسترداد بمفتاحه، والإشعار الموقَّع).
// ============================================================================
public class SecretProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmSecretProtector Protector(string active, params (string Id, string Key)[] keys) =>
        new(Options.Create(new SecretsSettings { ActiveKeyId = active, Keys = keys.ToDictionary(k => k.Id, k => k.Key) }));

    [Fact]
    public void يفكّ_ما_شفّره_لصاحبه_وحده_ولا_يحفظ_النصّ_ولا_يكرّر_النصّ_المشفّر()
    {
        var protector = Protector("k1", ("k1", NewKey()));
        var purpose = SecretPurposes.StripeSecretKey(5);

        var first = protector.Protect("sk_live_secret-value", purpose);
        var second = protector.Protect("sk_live_secret-value", purpose);

        protector.Unprotect(first, purpose).Should().Be("sk_live_secret-value");
        first.Should().StartWith("v1.k1.").And.NotContain("secret-value");
        first.Should().NotBe(second, "nonce عشوائي لكل تشفير");
        protector.Invoking(p => p.Unprotect(first, SecretPurposes.StripeSecretKey(6)))
            .Should().Throw<SecretUnavailableException>("نصّ منسوخ إلى صفّ متجر آخر لا يُفكّ");
        protector.Invoking(p => p.Unprotect(first, SecretPurposes.StripeWebhookSecret(5))).Should().Throw<SecretUnavailableException>();
    }

    [Fact]
    public void التدوير_يبقي_القديم_مقروءاً_والجديد_بالمفتاح_النشط_والمفتاح_المُزال_خطأ_ظاهر()
    {
        var (k1, k2) = (NewKey(), NewKey());
        var purpose = SecretPurposes.StripeWebhookSecret(3);
        var old = Protector("k1", ("k1", k1)).Protect("whsec_old", purpose);

        var rotated = Protector("k2", ("k1", k1), ("k2", k2));
        rotated.Unprotect(old, purpose).Should().Be("whsec_old");
        rotated.Protect("whsec_new", purpose).Should().StartWith("v1.k2.");

        Protector("k2", ("k2", k2)).Invoking(p => p.Unprotect(old, purpose)).Should().Throw<SecretUnavailableException>();
    }

    [Theory]
    [InlineData("v2.k1.AAAA")]
    [InlineData("k1.AAAA")]
    [InlineData("v1.k1.not-base64!")]
    [InlineData("v1.k1.AAAA")]
    public void النصّ_التالف_أو_المجهول_يُرفض(string tampered)
    {
        var protector = Protector("k1", ("k1", NewKey()));

        protector.Invoking(p => p.Unprotect(tampered, "p")).Should().Throw<SecretUnavailableException>();
    }

    [Fact]
    public void تعديل_بايت_واحد_يكسر_التحقّق()
    {
        var protector = Protector("k1", ("k1", NewKey()));
        var parts = protector.Protect("sk_test_value", "p").Split('.');
        var blob = Convert.FromBase64String(parts[2]);
        blob[^1] ^= 0x01;

        protector.Invoking(p => p.Unprotect($"{parts[0]}.{parts[1]}.{Convert.ToBase64String(blob)}", "p"))
            .Should().Throw<SecretUnavailableException>();
    }

    [Fact]
    public void بلا_إعداد_لا_تشفير_والإعداد_الخاطئ_يُرفض_عند_الإقلاع()
    {
        var unset = new AesGcmSecretProtector(Options.Create(new SecretsSettings()));
        unset.IsConfigured.Should().BeFalse();
        unset.Invoking(p => p.Protect("x", "p")).Should().Throw<SecretUnavailableException>();

        var validator = new SecretsSettingsValidator();
        validator.Validate(null, new SecretsSettings()).Succeeded.Should().BeTrue("اختياري: حساب النشر وحده");
        var composeUnset = new SecretsSettings { ActiveKeyId = "", Keys = new() { ["primary"] = "" } };
        validator.Validate(null, composeUnset).Succeeded.Should().BeTrue("Compose يمرّر المتغيّر فارغاً حين لا يُضبط");
        new AesGcmSecretProtector(Options.Create(composeUnset)).IsConfigured.Should().BeFalse();
        validator.Validate(null, new SecretsSettings { ActiveKeyId = "k1", Keys = new() { ["k1"] = "c2hvcnQ=" } })
            .Failed.Should().BeTrue("ليس 32 بايت");
        validator.Validate(null, new SecretsSettings { ActiveKeyId = "k9", Keys = new() { ["k1"] = NewKey() } })
            .Failed.Should().BeTrue("المفتاح النشط غير موجود");
        validator.Validate(null, new SecretsSettings { Keys = new() { ["k1"] = NewKey() } })
            .Failed.Should().BeTrue("مفاتيح بلا نشط");
        validator.Validate(null, new SecretsSettings { ActiveKeyId = "k.1", Keys = new() { ["k.1"] = NewKey() } })
            .Failed.Should().BeTrue("المعرّف جزء من الصيغة: حروف وأرقام فقط");
    }
}

public class FakeGatewayTests
{
    private const string Secret = "fake-webhook-secret";
    private readonly FakeGatewayLedger _ledger = new();

    [Fact]
    public async Task الاسترداد_بالمفتاح_نفسه_يعيد_الاسترداد_نفسه()
    {
        var gateway = new FakeGateway(_ledger, null);
        var amount = new Money(10, "JOD");

        var first = await gateway.RefundAsync("pi_fake_1", amount, "souq-refund-1-7", CancellationToken.None);
        var retry = await gateway.RefundAsync("pi_fake_1", amount, "souq-refund-1-7", CancellationToken.None);
        var other = await gateway.RefundAsync("pi_fake_1", amount, "souq-refund-1-8", CancellationToken.None);

        (first.Succeeded, retry.ProviderRefundId).Should().Be((true, first.ProviderRefundId));
        other.ProviderRefundId.Should().NotBe(first.ProviderRefundId);
        _ledger.RefundsFor("souq-refund-1-").Should().Be(2);
    }

    [Fact]
    public void الإشعار_التجريبي_يُقبل_موقَّعاً_فقط_ويحمل_متجره()
    {
        var gateway = new FakeGateway(_ledger, Secret);
        var payload = JsonSerializer.Serialize(new
        {
            type = "payment_intent.succeeded", paymentIntentId = "pi_fake_1", orderReference = "42", tenantId = 7,
        });

        gateway.ParseWebhook(payload, FakeGateway.Sign(payload, Secret)).Should().Be(new GatewayWebhookEvent("pi_fake_1", "42", 7));
        gateway.Invoking(g => g.ParseWebhook(payload, FakeGateway.Sign(payload, "another-secret")))
            .Should().Throw<InvalidPaymentWebhookException>();
        gateway.Invoking(g => g.ParseWebhook(payload, null)).Should().Throw<InvalidPaymentWebhookException>();

        var unrelated = JsonSerializer.Serialize(new { type = "charge.refunded", orderReference = "42" });
        gateway.ParseWebhook(unrelated, FakeGateway.Sign(unrelated, Secret)).Should().BeNull();
        new FakeGateway(_ledger, null).ParseWebhook(payload, "anything").Should().BeNull("بلا سرّ لا إشعارات");
    }
}
