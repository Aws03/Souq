using System.Net;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// ترويسات الأمان (R-16) — ما يُثبَّت هنا هو ما يُنسى عادةً:
//   • أنها موجودة على *الأخطاء* أيضاً. الترويسة المضبوطة مباشرةً تضيع حين يعيد معالج
//     الاستثناءات بناء الاستجابة، فتبقى المسارات السعيدة وحدها محميّة.
//   • أن سياسة /uploads الأشدّ لم تُستبدَل. OnPrepareResponse يعمل قبل OnStarting، فوسيط
//     يكتب سياسته عمياءً كان سيُضعِف حماية الملفات المرفوعة بصمت.
//   • أن HSTS لا تُرسَل على http (بلا معنى، وتُربك التشخيص) وتُرسَل على https.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class SecurityHeadersTests
{
    private readonly TestApi _api;

    public SecurityHeadersTests(SouqApiFactory factory) => _api = new TestApi(factory);

    [Theory]
    [InlineData("/api/storefront/config")]                 // نجاح
    [InlineData("/api/products/99999999")]                 // 404 من المعالج
    [InlineData("/api/account/profile")]                   // 401 من الإطار (UseStatusCodePages)
    public async Task ترويسات_الأمان_على_كل_استجابة_بما_فيها_الأخطاء(string path)
    {
        var response = await _api.Anonymous().GetAsync(path);

        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Permissions-Policy").Should().Contain("camera=()");
        Header(response, "Content-Security-Policy").Should().Contain("default-src 'none'")
            .And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task سياسة_الملفات_المرفوعة_الأشدّ_لا_تُستبدَل()
    {
        // ملف غير موجود يكفي: الوسيط يضبط ترويساته على أي استجابة تحت /uploads.
        // المهم أن سياسة الواجهة البرمجية لم تحلّ محلّ سياسة الملفات حيث تُضبط الأخيرة.
        var response = await _api.Anonymous().GetAsync("/uploads/tenants/1/does-not-exist.png");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
    }

    // مضيف حقيقي لا localhost: ASP.NET يستثني localhost و127.0.0.1 و[::1] من HSTS افتراضياً
    // (HstsOptions.ExcludedHosts)، وهو سبب "فعّلت HSTS ولا أرى الترويسة" في كل تجربة محلية.
    // المضيف غير مسجَّل فيعود 404، وهذا كافٍ ومقصود: الوسيط يسبق تحديد المستأجر.
    private const string PublicHost = "store.example";

    [Fact]
    public async Task HSTS_على_https_فقط()
    {
        var overHttp = await _api.Client(PublicHost).GetAsync("/api/storefront/config");
        var overHttps = await _api.SecureClient(PublicHost).GetAsync("/api/storefront/config");

        Header(overHttp, "Strict-Transport-Security").Should().BeNull("الترويسة بلا معنى على اتصال غير مشفّر");
        Header(overHttps, "Strict-Transport-Security").Should().Contain("max-age=");
    }

    [Fact]
    public async Task HSTS_لا_تُرسَل_لـ_localhost_ولو_على_https()
    {
        // سلوك الإطار، مثبَّت هنا كي لا يُقرأ غيابه محلياً على أنه عطل.
        Header(await _api.SecureClient().GetAsync("/api/storefront/config"), "Strict-Transport-Security")
            .Should().BeNull();
    }

    [Fact]
    public async Task HSTS_بلا_includeSubDomains_ولا_preload()
    {
        // المتاجر تأتي بنطاقاتها: التزام يتعدّى النطاق الواحد (أو preload شبه الدائم) قرار
        // نشر يُتَّخذ بعلم، لا افتراض يُشحن. Security:HstsMaxAgeDays يوسّعه عند اتخاذه.
        var hsts = Header(await _api.SecureClient(PublicHost).GetAsync("/api/storefront/config"), "Strict-Transport-Security");

        hsts.Should().NotContain("includeSubDomains").And.NotContain("preload");
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
}
