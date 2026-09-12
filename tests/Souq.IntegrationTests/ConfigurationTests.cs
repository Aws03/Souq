using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Services;

namespace Souq.IntegrationTests;

// ============================================================================
// اصطلاحات الإعداد (ADR-0020): إعداد ناقص أو ضعيف يرفض الإقلاع برسالة تسمّي المفتاح، ووسائل
// التطوير لا تعمل ضمنياً في الإنتاج. اختبارات الإقلاع هنا لا تحتاج قاعدة بيانات: الرفض يحدث
// قبل لمسها — وهذا بالضبط ما نثبته.
// ============================================================================
public class ConfigurationTests
{
    private const string ValidKey = "configuration-tests-only-signing-key-0123456789abcdef";

    [Theory]
    [InlineData(null, null, "Development", PaymentProvider.Fake)]
    [InlineData(null, null, "Testing", PaymentProvider.Fake)]
    [InlineData(null, "sk_test_x", "Production", PaymentProvider.Stripe)]
    [InlineData("Fake", null, "Production", PaymentProvider.Fake)]
    [InlineData("stripe", "sk_test_x", "Staging", PaymentProvider.Stripe)]
    public void اختيار_بوّابة_الدفع_صريح_خارج_التطوير(string? configured, string? key, string environment, PaymentProvider expected)
    {
        PaymentProviderSelector.Select(configured, key, environment).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, "Production", "Payments:Provider")]
    [InlineData("", "", "Staging", "Payments:Provider")]
    [InlineData("Stripe", null, "Development", "Stripe:SecretKey")]
    [InlineData("PayPal", "sk", "Production", "Payments:Provider")]
    public void لا_بوّابة_تجريبية_ضمنية_في_الإنتاج_ولا_قيم_مجهولة(string? configured, string? key, string environment, string mentions)
    {
        var act = () => PaymentProviderSelector.Select(configured, key, environment);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(mentions);
    }

    // R-17: قاعدة إنتاج جديدة كانت تُبذَر بمتجر تجريبي وكتالوجه ومظهره بلا أن يطلب أحد ذلك.
    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Testing", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Staging", null, false)]
    [InlineData("Production", true, true)]    // عرض توضيحي على خادم: بطلب صريح وحده
    [InlineData("Development", false, false)] // قاعدة تطوير نظيفة: الإعداد الصريح يغلب البيئة في الاتجاهين
    public void بيانات_العرض_لا_تُبذَر_خارج_التطوير_إلا_بطلب_صريح(string environment, bool? configured, bool expected)
    {
        DbSeeder.ShouldSeedDemoData(environment, configured).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "Souq", "SouqClient", 15, "Jwt:Key")]
    [InlineData("too-short-key", "Souq", "SouqClient", 15, "Jwt:Key")]
    [InlineData(ValidKey, "", "SouqClient", 15, "Jwt:Issuer")]
    [InlineData(ValidKey, "Souq", "", 15, "Jwt:Audience")]
    [InlineData(ValidKey, "Souq", "SouqClient", 0, "Jwt:ExpiryMinutes")]
    // ADR-0010: توكن الوصول قصير العمر — الجلسة الطويلة في رمز التجديد القابل للإبطال لا في JWT.
    [InlineData(ValidKey, "Souq", "SouqClient", 120, "Jwt:ExpiryMinutes")]
    public void إعدادات_JWT_الناقصة_أو_الضعيفة_تُرفض_باسم_المفتاح(string key, string issuer, string audience, int expiry, string mentions)
    {
        var result = new JwtSettingsValidator().Validate(null,
            new JwtSettings { Key = key, Issuer = issuer, Audience = audience, ExpiryMinutes = expiry });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(mentions);
        if (key.Length > 0) result.FailureMessage.Should().NotContain(key); // لا تُطبع قيمة السرّ
    }

    [Fact]
    public void إعدادات_JWT_السليمة_مقبولة()
    {
        new JwtSettingsValidator().Validate(null,
                new JwtSettings { Key = ValidKey, Issuer = "Souq", Audience = "SouqClient", ExpiryMinutes = 15 })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void مفتاح_Stripe_العلني_مطلوب_خارج_التطوير_فقط()
    {
        var settings = new StripeSettings { SecretKey = "sk_test_x" };

        new StripeSettingsValidator(requirePublishableKey: true).Validate(null, settings)
            .FailureMessage.Should().Contain("Stripe:PublishableKey");
        new StripeSettingsValidator(requirePublishableKey: false).Validate(null, settings).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void الإقلاع_في_Production_بلا_بوّابة_دفع_يُرفض_قبل_لمس_القاعدة()
    {
        using var factory = new ConfiguredFactory("Production", ("Jwt:Key", ValidKey));

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Which.ToString().Should().Contain("Payments:Provider");
    }

    [Fact]
    public void مفتاح_JWT_قصير_يرفض_الإقلاع_قبل_لمس_القاعدة()
    {
        using var factory = new ConfiguredFactory("Testing", ("Jwt:Key", "short-signing-key"));

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Which.ToString().Should().Contain("Jwt:Key");
    }

    [Fact]
    public void بلا_سلسلة_اتصال_يُرفض_الإقلاع_برسالة_تسمّي_المفتاح()
    {
        using var factory = new ConfiguredFactory("Testing", ("Jwt:Key", ValidKey), ("ConnectionStrings:Default", ""));

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Which.ToString().Should().Contain("ConnectionStrings:Default");
    }

    // مصنع بإعداد يُحدَّد لكل اختبار. سلسلة اتصال لخادم غير موجود: إن وصل الإقلاع للقاعدة لفشل
    // الاختبار بخطأ اتصال لا برسالة الإعداد المتوقّعة.
    private sealed class ConfiguredFactory(string environment, params (string Key, string Value)[] settings)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Default",
                "Server=127.0.0.1,1;Database=unreachable;User ID=x;Password=y;TrustServerCertificate=True;Connect Timeout=1");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        }
    }
}
