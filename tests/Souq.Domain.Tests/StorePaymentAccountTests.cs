using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Entities;

namespace Souq.Domain.Tests;

// ============================================================================
// حساب بوّابة المتجر (المرحلة 11، D-13): صيغ مفاتيح Stripe ووضعها (تجريبي/حقيقي)، أول ربط يلزمه سرّ، التعديل بلا سرّ يبقي
// المحفوظ، ولا خلط لوضعين — تغيير الوضع يلزمه سرّ جديد. الكيان لا يرى نصّ السرّ أبداً: يُعطى نصّاً مشفّراً وتلميحاً.
// ============================================================================
public class StorePaymentAccountTests
{
    private const string TestPublishable = "pk_test_51Habcdefghijk";
    private const string LivePublishable = "pk_live_51Habcdefghijk";

    [Theory]
    [InlineData(TestPublishable, false)]
    [InlineData(LivePublishable, true)]
    [InlineData("  pk_test_51Habcdefghijk  ", false)]
    [InlineData("sk_test_51Habcdefghijk", null)]
    [InlineData("pk_test_", null)]
    [InlineData("pk_test_51Habc defghijk", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void صيغة_المفتاح_العلني_ووضعه(string? key, bool? live) =>
        PaymentKeyRules.PublishableMode(key).Should().Be(live);

    [Theory]
    [InlineData("sk_test_51Habcdefghijk", false)]
    [InlineData("sk_live_51Habcdefghijk", true)]
    [InlineData("rk_test_51Habcdefghijk", false)]
    [InlineData("rk_live_51Habcdefghijk", true)]
    [InlineData("pk_live_51Habcdefghijk", null)]
    [InlineData("whsec_51Habcdefghijk", null)]
    public void صيغة_المفتاح_السرّي_ووضعه(string key, bool? live) =>
        PaymentKeyRules.SecretMode(key).Should().Be(live);

    [Fact]
    public void سرّ_الإشعارات_بصيغته_والتلميح_آخر_أربعة_محارف_فقط()
    {
        (PaymentKeyRules.IsWebhookSecret("whsec_abcdefghij"), PaymentKeyRules.IsWebhookSecret("whsec_"),
            PaymentKeyRules.IsWebhookSecret("sk_test_abcdefghij")).Should().Be((true, false, false));
        PaymentKeyRules.Hint("sk_test_51Habcdefghijk").Should().Be("…hijk");
    }

    [Fact]
    public void أول_ربط_يلزمه_سرّ_والوضع_يطابق_المفتاح_العلني()
    {
        var withoutSecret = () => new StorePaymentAccount(TestPublishable, "", "…abcd", false, null, 1);
        var mixedModes = () => new StorePaymentAccount(LivePublishable, "v1.k1.cipher", "…abcd", liveMode: false, null, 1);
        var notStripe = () => new StorePaymentAccount("publishable", "v1.k1.cipher", "…abcd", false, null, 1);

        withoutSecret.Should().Throw<InvalidPaymentOperationException>().Which.Code.Should().Be("InvalidPaymentKeys");
        mixedModes.Should().Throw<InvalidPaymentOperationException>();
        notStripe.Should().Throw<InvalidPaymentOperationException>();

        var account = new StorePaymentAccount(TestPublishable, "v1.k1.cipher", "…abcd", false, null, 1);
        (account.Provider, account.LiveMode, account.SecretKeyHint, account.WebhookSecretCipher, account.UpdatedByUserId)
            .Should().Be((StorePaymentAccount.Stripe, false, "…abcd", (string?)null, (int?)1));
    }

    [Fact]
    public void التعديل_بلا_سرّ_يبقي_المحفوظ_وتغيير_الوضع_يلزمه_سرّ_جديد()
    {
        var account = new StorePaymentAccount(TestPublishable, "v1.k1.old", "…1111", false, null, 1);

        account.Update("pk_test_51Hzzzzzzzzzz", false, null, null, "v1.k1.hook", 2);
        (account.PublishableKey, account.SecretKeyCipher, account.SecretKeyHint, account.WebhookSecretCipher, account.UpdatedByUserId)
            .Should().Be(("pk_test_51Hzzzzzzzzzz", "v1.k1.old", "…1111", "v1.k1.hook", (int?)2));

        account.Invoking(a => a.Update("pk_live_51Hzzzzzzzzzz", true, null, null, null, 2))
            .Should().Throw<InvalidPaymentOperationException>();
        account.LiveMode.Should().BeFalse("تعديل مرفوض لا يغيّر شيئاً");

        account.Update("pk_live_51Hzzzzzzzzzz", true, "v1.k1.live", "…2222", null, 2);
        (account.LiveMode, account.SecretKeyCipher, account.SecretKeyHint, account.WebhookSecretCipher)
            .Should().Be((true, "v1.k1.live", "…2222", "v1.k1.hook"));
    }
}
