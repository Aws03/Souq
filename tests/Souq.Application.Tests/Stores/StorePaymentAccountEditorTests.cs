using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Stores;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.Entities;

namespace Souq.Application.Tests.Stores;

// ============================================================================
// حساب بوّابة المتجر (المرحلة 11، D-13): السرّان يُشفَّران مربوطَين بالمتجر ولا يُحفظ نصّهما، المفاتيح التجريبية لا تُقبل
// حيث تمنعها السياسة (دفع وهمي صامت)، الصيغ الخاطئة ترفض برمز واحد، التعديل بلا سرّ يبقي المحفوظ، ولا سرّ في أي قراءة.
// ============================================================================
public class StorePaymentAccountEditorTests
{
    private const string Pk = "pk_test_51Habcdefghijk";
    private const string Sk = "sk_test_51Habcdefghijk";
    private const string Hook = "whsec_abcdefghijklmn";

    private readonly IStorePaymentAccountRepository _accounts = Substitute.For<IStorePaymentAccountRepository>();
    private readonly ISecretProtector _secrets = Substitute.For<ISecretProtector>();
    private readonly Souq.Domain.Interfaces.IUnitOfWork _uow = TestUnitOfWork.Create();

    public StorePaymentAccountEditorTests()
    {
        _secrets.IsConfigured.Returns(true);
        _secrets.Protect(Arg.Any<string>(), Arg.Any<string>()).Returns(call => $"cipher[{call.ArgAt<string>(1)}]");
    }

    private StorePaymentAccountEditor Editor(bool allowTestKeys = true) => new(
        _accounts, _secrets, TestTenant.Context(id: 5), TestCurrentUser.Admin(),
        new StorePaymentPolicy { AllowTestKeys = allowTestKeys }, _uow);

    [Fact]
    public async Task الربط_الأول_يشفّر_السرّين_مربوطَين_بالمتجر_ولا_يحفظ_نصّهما()
    {
        StorePaymentAccount? added = null;
        _accounts.When(a => a.AddAsync(Arg.Any<StorePaymentAccount>(), Arg.Any<CancellationToken>()))
            .Do(call => added = call.Arg<StorePaymentAccount>());

        var result = await Editor().SaveAsync(new StorePaymentAccountInput($" {Pk} ", Sk, Hook), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (added!.PublishableKey, added.SecretKeyCipher, added.WebhookSecretCipher, added.SecretKeyHint, added.LiveMode)
            .Should().Be((Pk, "cipher[tenant:5:stripe:secret-key]", "cipher[tenant:5:stripe:webhook-secret]", "…hijk", false));
        _secrets.Received(1).Protect(Sk, SecretPurposes.StripeSecretKey(5));
        _secrets.Received(1).Protect(Hook, SecretPurposes.StripeWebhookSecret(5));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task المفاتيح_التجريبية_تُرفض_حيث_تمنعها_السياسة_والحقيقية_تُقبل()
    {
        var refused = await Editor(allowTestKeys: false).SaveAsync(new StorePaymentAccountInput(Pk, Sk, null), CancellationToken.None);

        refused.ErrorCode.Should().Be("TestKeysNotAllowed");
        _secrets.DidNotReceiveWithAnyArgs().Protect(default!, default!);
        await _accounts.DidNotReceiveWithAnyArgs().AddAsync(default!, default);

        var live = await Editor(allowTestKeys: false).SaveAsync(
            new StorePaymentAccountInput("pk_live_51Habcdefghijk", "rk_live_51Habcdefghijk", null), CancellationToken.None);
        live.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("sk_test_51Habcdefghijk", Sk, null)]                  // ليس مفتاحاً علنياً
    [InlineData("pk_live_51Habcdefghijk", Sk, null)]                  // وضعان مختلفان
    [InlineData(Pk, "not-a-stripe-secret", null)]
    [InlineData(Pk, Sk, "not-a-webhook-secret")]
    [InlineData(Pk, null, null)]                                      // أول ربط بلا سرّ
    public async Task الصيغ_الخاطئة_تُرفض_برمز_واحد_ولا_يُحفظ_شيء(string publishable, string? secret, string? webhook)
    {
        var act = () => Editor().SaveAsync(new StorePaymentAccountInput(publishable, secret, webhook), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidPaymentOperationException>()).Which.Code.Should().Be("InvalidPaymentKeys");
        await _accounts.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task بلا_تشفير_مضبوط_على_الخادم_لا_يُحفظ_حساب()
    {
        _secrets.IsConfigured.Returns(false);

        var result = await Editor().SaveAsync(new StorePaymentAccountInput(Pk, Sk, null), CancellationToken.None);

        result.ErrorCode.Should().Be("SecretsNotConfigured");
    }

    [Fact]
    public async Task التعديل_بلا_سرّ_يبقي_المحفوظ_والقراءة_بلا_سرّ()
    {
        var existing = new StorePaymentAccount(Pk, "cipher-old", "…1111", false, "cipher-hook", 1);
        _accounts.GetAsync(Arg.Any<CancellationToken>()).Returns(existing);

        (await Editor().SaveAsync(new StorePaymentAccountInput("pk_test_51Hzzzzzzzzzz", null, " "), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        (existing.PublishableKey, existing.SecretKeyCipher, existing.WebhookSecretCipher)
            .Should().Be(("pk_test_51Hzzzzzzzzzz", "cipher-old", "cipher-hook"));
        _secrets.DidNotReceiveWithAnyArgs().Protect(default!, default!);

        var view = await Editor().GetAsync(CancellationToken.None);
        (view.UsesStoreAccount, view.SecretKeyHint, view.HasWebhookSecret, view.LiveMode, view.CanStoreSecrets)
            .Should().Be((true, "…1111", true, (bool?)false, true));
        typeof(StorePaymentAccountDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Cipher") || name == "SecretKey" || name == "WebhookSecret");
    }

    [Fact]
    public async Task فكّ_الربط_يعيد_حساب_النشر_ومضمون_التكرار()
    {
        var existing = new StorePaymentAccount(Pk, "cipher", "…1111", false, null, 1);
        _accounts.GetAsync(Arg.Any<CancellationToken>()).Returns(existing, (StorePaymentAccount?)null);

        (await Editor().RemoveAsync(CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await Editor().RemoveAsync(CancellationToken.None)).IsSuccess.Should().BeTrue();

        _accounts.Received(1).Remove(existing);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
