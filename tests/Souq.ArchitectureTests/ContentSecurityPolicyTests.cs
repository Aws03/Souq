using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Souq.ArchitectureTests;

// ============================================================================
// سياسة محتوى الواجهة تُشحن بوضع الإبلاغ (Report-Only) حتى تؤكّدها جلسة متصفّح واحدة. الخطر
// في تلك الفترة ليس السياسة نفسها بل *انحرافها*: يضيف أحدهم خطّاً أو سكربت تحليلات من أصل
// جديد، فتبقى السياسة كما هي — لا تكسر شيئاً لأنها لا تفرض، ثم تُفرَض يوماً فتكسر ما لم يُذكر
// فيها. عندها يُلام التفعيل لا الإغفال.
//
// هذا الاختبار يجعل الإضافة مرئية وقت البناء: كل أصل خارجي تشير إليه شيفرة الواجهة يجب أن
// يكون مذكوراً في السياسة. نطاقات .example محجوزة للتوثيق (RFC 2606) وتُستعمل في بيانات
// الاختبار، فلا تُحسب.
// ============================================================================
public class ContentSecurityPolicyTests
{
    private static readonly Regex Origin = new(@"https://(?<host>[a-zA-Z0-9.-]+)", RegexOptions.Compiled);

    // أصول تُحمَّل في زمن التشغيل بلا أن تظهر نصّاً في المصدر: مكتبة Stripe تحقن سكربتها
    // وإطاراتها عند الدفع. مذكورة هنا كي يبقى الفحص أعلى من "ما يظهر في grep".
    private static readonly string[] RuntimeInjectedOrigins = ["js.stripe.com", "api.stripe.com", "hooks.stripe.com"];

    private static string Policy() =>
        File.ReadAllText(RepositoryPaths.Combine("frontend/nginx.conf"));

    [Fact]
    public void كل_أصل_خارجي_تستعمله_الواجهة_مذكور_في_سياسة_المحتوى()
    {
        var policy = Policy();

        var referenced = RepositoryPaths.Walk("frontend/src")
            .Where(f => f.EndsWith(".js", StringComparison.Ordinal) || f.EndsWith(".jsx", StringComparison.Ordinal))
            .Where(f => !f.Contains(".test.", StringComparison.Ordinal))
            .Append(RepositoryPaths.Combine("frontend/index.html"))
            .SelectMany(f => Origin.Matches(File.ReadAllText(f)).Select(m => m.Groups["host"].Value))
            .Concat(RuntimeInjectedOrigins)
            .Where(host => !host.EndsWith(".example", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        referenced.Should().NotBeEmpty("الواجهة تحمّل خطوطاً وStripe — فحص لا يجد شيئاً فحصٌ معطّل");

        referenced.Where(host => !policy.Contains(host, StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("أصل تستعمله الواجهة وليس في السياسة سيُحجب لحظة تفعيلها");
    }

    [Fact]
    public void السياسة_ما_زالت_بوضع_الإبلاغ_ولها_مالك_معروف()
    {
        // حين تُفرَض، يجب أن يتغيّر هذا الاختبار عمداً — لا أن يمرّ صامتاً في الاتجاهين.
        var policy = Policy();

        policy.Should().Contain("Content-Security-Policy-Report-Only",
            "تفعيل السياسة قرار نشر يحتاج جلسة متصفّح واحدة أولاً (ReleaseReadiness)");
        policy.Should().Contain("frame-ancestors 'none'", "منع التأطير لا ينتظر شيئاً ويجب أن يبقى");
        policy.Should().Contain("object-src 'none'");
    }

    [Fact]
    public void سياسة_الواجهة_البرمجية_تبقى_الأضيق()
    {
        // استجابة JSON لا تحمّل شيئاً إطلاقاً؛ توسيعها يوماً يجب أن يكون قراراً مرئياً.
        var middleware = File.ReadAllText(
            RepositoryPaths.Combine("src/Souq.API/Security/SecurityHeadersMiddleware.cs"));

        middleware.Should().Contain("default-src 'none'");
        middleware.Should().Contain("frame-ancestors 'none'");
        middleware.Should().Contain("form-action 'none'");
    }
}
