using System.Diagnostics;
using Souq.API.Http;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;

namespace Souq.API.Observability;

// ============================================================================
// سطر سجلّ واحد لكل طلب HTTP (ADR-0018): الطريقة، المسار بلا سلسلة الاستعلام، قالب المسار
// (للتجميع: /api/orders/{id:int} لا آلاف الأسطر المختلفة)، الرمز، والمدّة. ويفتح نطاق سجلّ
// يحمل CorrelationId وUserId فيرثه كل سجلّ داخل الطلب (المعالجات، EF، المزوّدات).
//
// لا يُسجَّل أبداً: سلسلة الاستعلام، الترويسات (Authorization)، الأجسام (كلمات مرور، رموز).
// مكانه بعد UseAuthentication كي يعرف المستخدم، وقبل UseAuthorization كي يُسجَّل 401/403 أيضاً.
// النطاق يحمل TenantId أيضاً (المرحلة 2) — كل سجلّ يُعرف متجره بلا جهد في الميزات.
// ============================================================================
public sealed class RequestLoggingMiddleware
{
    private const int ClientClosedRequest = 499;

    // قيم مسار سرّية بطبيعتها: من يحملها يفتح المورد بلا مصادقة. رمز تتبّع الطلب جزء من المسار
    // (/api/orders/track/{token})، فكان يُكتب كاملاً في سطر السجلّ — ومن يقرأ السجلّ يفتح صفحة تتبّع أي عميل (R-10).
    // يُنقَّح بالقيمة لا بموضعها في المسار، فيبقى السطر مفيداً للتشخيص (الطريقة والقالب والرمز والمدّة).
    private static readonly string[] SensitiveRouteValues = ["token"];
    private const string Redacted = "***";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, ITenantContext tenancy)
    {
        using var scope = _logger.BeginScope(ScopeFor(context, currentUser, tenancy));
        var started = Stopwatch.GetTimestamp();
        int? statusOverride = null;
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            statusOverride = ClientClosedRequest;   // العميل أغلق الاتصال — ليس خطأ خادم
            throw;
        }
        catch
        {
            statusOverride = StatusCodes.Status500InternalServerError; // المعالج الخارجي سيكتب 500
            throw;
        }
        finally
        {
            var status = statusOverride ?? context.Response.StatusCode;
            _logger.Log(status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Information,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms (route {Route})",
                context.Request.Method, RedactedPath(context), status,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(none)");
        }
    }

    // يعمل بعد التوجيه، فقيم المسار متاحة. لا مطابقة نصّية عمياء: نستبدل قيمة المعامل الحسّاس نفسها وحدها.
    private static string RedactedPath(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        foreach (var name in SensitiveRouteValues)
            if (context.Request.RouteValues.TryGetValue(name, out var value)
                && value is string secret && secret.Length > 0)
                path = path.Replace(secret, Redacted, StringComparison.Ordinal);
        return path;
    }

    private static Dictionary<string, object?> ScopeFor(HttpContext context, ICurrentUser currentUser, ITenantContext tenancy)
    {
        var scope = new Dictionary<string, object?> { ["CorrelationId"] = RequestCorrelation.GetId(context) };
        if (tenancy.Tenant is { } tenant)
            scope["TenantId"] = tenant.Id;
        else if (tenancy.Scope == TenantScope.Platform)
            scope["Area"] = "Platform";
        if (currentUser.UserId is int userId)
            scope["UserId"] = userId;
        return scope;
    }
}
