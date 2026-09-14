using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Souq.Infrastructure.Persistence;

namespace Souq.API.Observability;

// ============================================================================
// الفحوص الصحّية — مسباران بمعنيين مختلفين، والخلط بينهما هو الخطأ الشائع:
//   الحيوية  (live)  : هل العملية حيّة؟ لا تبعية خارجية إطلاقاً. فشلها ⇒ "أعد تشغيل الحاوية".
//                      لو فحصت القاعدة، لأعاد المنظّم تشغيل كل نسخ الـ API عند أول تعثّر في
//                      القاعدة — يقتل ما كان سيتعافى وحده ويحوّل عطلاً جزئياً إلى انقطاع كامل.
//   الجاهزية (ready) : هل تستطيع هذه النسخة خدمة طلب حقيقي الآن؟ فشلها ⇒ "لا ترسل لي طلبات"
//                      بلا إعادة تشغيل.
// كلاهما مجهول الهوية وخارج /api عمداً: مسبار المنظّم يصل بمضيف الحاوية لا بمضيف متجر،
// و TenantResolutionMiddleware يردّ 404 لأي مضيف غير مسجَّل. (يعمل الوسيط على /api و/uploads
// فقط، فالمسار خارجهما أصلاً — والترتيب في Program.cs يجعل ذلك قراراً صريحاً، ويثبّته اختبار.)
// ============================================================================
public static class HealthEndpoints
{
    public const string Live = "/health/live";
    public const string Ready = "/health/ready";

    // وسم الفحوص التي تدخل في الجاهزية وحدها.
    public const string ReadyTag = "ready";

    // جسم مختصر عمداً: الحالة العامة وحالة كل فحص بالاسم — بلا أوصاف ولا استثناءات ولا
    // مدد تنفيذ. النقطة مجهولة الهوية، والسبب التفصيلي يذهب إلى السجل (DatabaseHealthCheck).
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.Headers.CacheControl = "no-store";
        var body = new HealthBody(
            report.Status.ToString(),
            report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()));
        return context.Response.WriteAsJsonAsync(body);
    }

    public static HealthCheckOptions Liveness() => new()
    {
        Predicate = _ => false,     // لا فحوص: الردّ نفسه هو الدليل.
        ResponseWriter = WriteAsync,
    };

    public static HealthCheckOptions Readiness() => new()
    {
        Predicate = check => check.Tags.Contains(ReadyTag),
        ResponseWriter = WriteAsync,
    };
}

public sealed record HealthBody(string Status, IReadOnlyDictionary<string, string> Checks);

// ============================================================================
// DatabaseHealthCheck — فحص الجاهزية الوحيد اليوم:
//   لا اتصال بالقاعدة ⇒ Unhealthy (لا شيء يعمل بدونها).
//   هجرة معلّقة       ⇒ Unhealthy: المخطّط أقدم مما يتوقّعه هذا الإصدار — بعد استعادة نسخة
//                       احتياطية أقدم من الإصدار المنشور مثلاً (R-19). خدمة الطلبات على مخطّط
//                       مجهول أخطر من رفضها بوضوح، والهجرات تُطبَّق عند الإقلاع لا عند الطلب.
// لا يقرأ أي كيان: CanConnect و GetPendingMigrations لا يمسّان مرشّح المستأجر، فالفحص يعمل
// بلا متجر في السياق (Scope = None) — وهي حال مسبار الحاوية دائماً.
// ============================================================================
public sealed class DatabaseHealthCheck(AppDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            logger.LogWarning("Readiness failed: the database is not reachable");
            return HealthCheckResult.Unhealthy("database unreachable");
        }

        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pending.FirstOrDefault() is not { } first) return HealthCheckResult.Healthy();

        logger.LogWarning("Readiness failed: {PendingMigrationCount} migration(s) pending, first {PendingMigration}",
            pending.Count(), first);
        return HealthCheckResult.Unhealthy("schema out of date");
    }
}

// ============================================================================
// HealthProbe — مسبار الحاوية نفسها. صورة aspnet:10.0 لا تحوي curl ولا wget، وإضافة أحدهما
// تضع عميل HTTP كامل في صورة الإنتاج (أداة جاهزة للمهاجم بعد أي اختراق) لمجرّد الفحص. بدل
// ذلك يفحص التطبيق نفسه بزمن التشغيل الموجود أصلاً:
//   dotnet Souq.API.dll --health-check   ⇒ 0 جاهز، 1 غير ذلك.
// يُستدعى من HEALTHCHECK في Dockerfile. يسبق بناء أي خدمة في Program.cs: لا إعداد، لا قاعدة،
// لا أسرار — طلب HTTP واحد إلى هذه النسخة على مضيفها المحلي.
// ============================================================================
public static class HealthProbe
{
    public const string Argument = "--health-check";

    public static async Task<int> RunAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        return await ProbeAsync(client, $"http://127.0.0.1:{Port()}{HealthEndpoints.Ready}");
    }

    public static async Task<int> ProbeAsync(HttpClient client, string url)
    {
        try
        {
            return (await client.GetAsync(url)).IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception)
        {
            return 1;   // تعذّر الوصول = غير جاهز. لا تفصيل: المسبار يتكلّم برمز الخروج وحده.
        }
    }

    // نفس المنفذ الذي يضبطه Dockerfile (ASPNETCORE_HTTP_PORTS)، وافتراضي ASP.NET Core خلفه.
    private static string Port() =>
        Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0].Trim() is { Length: > 0 } port
            ? port
            : "8080";
}
