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
    [InlineData(null, null, "Development", PaymentProvider.Demo)]
    [InlineData(null, null, "Testing", PaymentProvider.Demo)]
    [InlineData(null, "sk_test_x", "Production", PaymentProvider.Stripe)]
    [InlineData("Demo", null, "Production", PaymentProvider.Demo)]
    // الاسم القديم يبقى مقبولاً: إعدادٌ مكتوب قبل إعادة التسمية لا يُسقط إقلاعاً.
    [InlineData("Fake", null, "Production", PaymentProvider.Demo)]
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

    // ── M6: القيم النائبة المنشورة في .env.example لا يجوز أن تُنتج نشراً عاملاً ─────────
    [Theory]
    [InlineData("REPLACE_WITH_A_LONG_RANDOM_SECRET")]           // القيمة الحرفية في .env.example
    [InlineData("change_me_change_me_change_me_change_me")]
    [InlineData("YOUR_SECRET_KEY_GOES_HERE_1234567890123")]
    public void مفتاح_JWT_النائب_يُرفض_رغم_أنه_طويل_بما_يكفي(string placeholder)
    {
        // الخطر الحقيقي: هذه القيم تتجاوز حدّ الـ32 بايت، فنسخ .env.example كما هو كان يُنتج
        // نشراً مفتاحُ توقيعه منشور في المستودع — ومن يقرؤه يزوّر توكن أي مستأجر.
        System.Text.Encoding.UTF8.GetByteCount(placeholder).Should()
            .BeGreaterThanOrEqualTo(JwtSettingsValidator.MinimumKeyBytes, "وإلا لكان فحص الطول كافياً");

        var result = new JwtSettingsValidator().Validate(null,
            new JwtSettings { Key = placeholder, Issuer = "Souq", Audience = "SouqClient", ExpiryMinutes = 15 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Jwt:Key");
    }

    [Fact]
    public void مفتاح_عشوائي_حقيقي_لا_يُخطئ_به_الفحص()
    {
        var real = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

        new JwtSettingsValidator().Validate(null,
                new JwtSettings { Key = real, Issuer = "Souq", Audience = "SouqClient", ExpiryMinutes = 15 })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ملف_env_example_نفسه_لا_يحوي_قيمة_صالحة_للاستعمال()
    {
        // الملف جزء من العقد: لو صار أحد أمثلته صالحاً، عاد الخطر نفسه.
        var example = File.ReadAllLines(RepositoryRoot() + "/.env.example");
        var jwt = example.First(l => l.StartsWith("JWT_KEY=", StringComparison.Ordinal))["JWT_KEY=".Length..];

        JwtSettingsValidator.LooksLikePlaceholder(jwt).Should().BeTrue("المثال يجب أن يبقى مرفوضاً عند الإقلاع");
    }

    // ── M6: لا بريد طرفي صامت خارج التطوير ──────────────────────────────────────────
    [Fact]
    public void بلا_مزوّد_بريد_خارج_التطوير_يُرفض_الإقلاع_ويُسمّى_المفتاح()
    {
        using var factory = new ConfiguredFactory("Production",
            ("Jwt:Key", ValidKey), ("Payments:Provider", "Fake"));

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Which.ToString().Should().Contain("Resend:ApiKey");
    }

    [Fact]
    public void بريد_السجل_مقبول_خارج_التطوير_بطلب_صريح_وحده()
    {
        // Email:Provider=Log يمرّ، لكن الإقلاع يستمر حتى القاعدة (غير موجودة هنا) — فالفشل
        // المتوقّع صار فشل اتصال لا فشل إعداد. هذا هو الفرق الذي نثبته.
        //
        // **وهذه التوليفة بعينها هي ما يحمله `.env.example`**: `Demo` + `Log` في بيئة Production.
        // فهذا الاختبار هو ما يُثبت أنّ `cp .env.example .env && docker compose up` يقلع عند مَن
        // يراجع المشروع بلا حسابٍ عند أيّ مزوّد — وهو أوّلُ ما يجرّبه، وأسوأُ ما يمكن أن يفشل.
        using var factory = new ConfiguredFactory("Production",
            ("Jwt:Key", ValidKey), ("Payments:Provider", "Demo"), ("Email:Provider", "Log"));

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Which.ToString().Should().NotContain("Resend:ApiKey");
    }

    // ── M6: أصل تطويري لا يتسرّب إلى الإنتاج ────────────────────────────────────────
    [Theory]
    [InlineData(true, 1)]    // تطوير/اختبار: أصل Vite المحلي
    [InlineData(false, 0)]   // غيرهما: لا أصل افتراضي إطلاقاً
    public void أصول_CORS_الافتراضية_تتبع_البيئة(bool local, int expected)
    {
        Souq.API.Http.CorsOrigins.For(null, local).Should().HaveCount(expected);
        Souq.API.Http.CorsOrigins.For([], local).Should().HaveCount(expected);
    }

    [Fact]
    public void إعداد_CORS_الصريح_يغلب_في_كل_البيئات()
    {
        string[] configured = ["https://store.example"];

        Souq.API.Http.CorsOrigins.For(configured, localEnvironment: false).Should().BeEquivalentTo(configured);
        Souq.API.Http.CorsOrigins.For(configured, localEnvironment: true).Should().BeEquivalentTo(configured);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Souq.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("جذر المستودع غير موجود");
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

    // ========================================================================
    // TD-63 — أعمار الجلسة مثبَّتة **بأرقامها** لا بثوابتها.
    //
    // الاختبارات أعلاه تقيس أنّ الإعداد **يُتحقَّق منه**: مدّة صفر أو 120 دقيقة تُرفض. ولا شيء
    // يقول ما هي المدّة الافتراضية فعلاً. فنشرٌ لا يضبط `Jwt:ExpiryMinutes` يأخذ ما في الشيفرة،
    // وتغييرُ ذلك الرقم — عمداً أو في دمج — لا يُفشل شيئاً ولا يُلاحَظ.
    //
    // وهو رقم أمني: عمر توكن الوصول هو المدّة التي يبقى فيها توكنٌ مسرَّب نافعاً، وعمر رمز
    // التجديد هو أقصى عمر جلسة لم يُسجَّل خروجها.
    // ========================================================================
    [Fact]
    public void أعمار_الجلسة_الافتراضية_هي_المعلنة()
    {
        var defaults = new JwtSettings();

        defaults.ExpiryMinutes.Should().Be(15,
            "عمر توكن الوصول هو مدّة نفع توكنٍ مسرَّب — إطالته قرارٌ يُتخذ لا تغييرٌ يمرّ");
        defaults.RefreshTokenDays.Should().Be(30,
            "وعمر رمز التجديد هو أقصى عمر جلسة لم يُسجَّل خروجها");
    }
}
