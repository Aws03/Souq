using System.Diagnostics;
using Souq.API.Http;
using Souq.Application.Common.Security;

namespace Souq.API.Observability;

// ============================================================================
// سطر سجلّ واحد لكل طلب HTTP (ADR-0018): الطريقة، المسار بلا سلسلة الاستعلام، قالب المسار
// (للتجميع: /api/orders/{id:int} لا آلاف الأسطر المختلفة)، الرمز، والمدّة. ويفتح نطاق سجلّ
// يحمل CorrelationId وUserId فيرثه كل سجلّ داخل الطلب (المعالجات، EF، المزوّدات).
//
// لا يُسجَّل أبداً: سلسلة الاستعلام، الترويسات (Authorization)، الأجسام (كلمات مرور، رموز).
// مكانه بعد UseAuthentication كي يعرف المستخدم، وقبل UseAuthorization كي يُسجَّل 401/403 أيضاً.
// المرحلة 2 تضيف TenantId إلى هذا النطاق نفسه — كل سجلّ يُعرف مستأجره بلا جهد في الميزات.
// ============================================================================
public sealed class RequestLoggingMiddleware
{
    private const int ClientClosedRequest = 499;

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        using var scope = _logger.BeginScope(ScopeFor(context, currentUser));
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
                context.Request.Method, context.Request.Path.Value, status,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(none)");
        }
    }

    private static Dictionary<string, object?> ScopeFor(HttpContext context, ICurrentUser currentUser)
    {
        var scope = new Dictionary<string, object?> { ["CorrelationId"] = RequestCorrelation.GetId(context) };
        if (currentUser.UserId is int userId)
            scope["UserId"] = userId;
        return scope;
    }
}
