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
        catch (Exception exception)
        {
            // ====================================================================
            // الرمز الذي **سيكتبه** المعالج، لا 500 افتراضاً.
            //
            // هذا السطر يُنفَّذ قبل أن يُقرَّر الرمز: `UseExceptionHandler` مسجَّل قبل هذه الطبقة
            // (Program.cs)، فالاستثناء يمرّ من هنا صاعداً ثمّ يترجمه GlobalExceptionHandler إلى
            // 401/403/409/422/503. وكان الافتراض «الخارجي سيكتب 500» يعني أن كل رفضٍ مشروع —
            // مشترٍ يطلب كمّية أكبر من المخزون (422)، موظّف متجر يفتح صفحة عميل (403)، تعارض
            // rowversion (409) — يُسجَّل «500» بمستوى Error. فكان السجلّ يناقض ما استلمه العميل،
            // ويُغرق معدّل الأخطاء بضجيج يُنذَر عنه ويُخفي الأعطال الحقيقية.
            // قِيس على الحزمة: طلب ردّ 403 على العميل وسُجّل 500 بمعرّف الربط نفسه.
            //
            // المصدر واحد عمداً — دالّة الترجمة ذاتها — كي لا ينفصل ما يُسجَّل عمّا يُرسَل.
            // ====================================================================
            statusOverride = Middleware.GlobalExceptionHandler.ToProblem(exception).Status
                             ?? StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var status = statusOverride ?? context.Response.StatusCode;
            _logger.Log(status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Information,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms (route {Route})",
                context.Request.Method, SensitivePath.Redact(context), status,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(none)");
        }
    }

    // عامّة كي تُختبر مباشرةً، كـ `ProxyTrustDiagnostics.WasForwardedProtoIgnored` ولنفس السبب:
    // `Connection.RemoteIpAddress` في خادم الاختبار داخل العملية **null** — لا اتصال حقيقي هناك —
    // فلا يمكن إثبات وصول العنوان إلى النطاق عبر طلبٍ في TestServer. الدالّة تُفحص بسياقٍ له عنوان.
    public static Dictionary<string, object?> ScopeFor(HttpContext context, ICurrentUser currentUser, ITenantContext tenancy)
    {
        var scope = new Dictionary<string, object?> { ["CorrelationId"] = RequestCorrelation.GetId(context) };
        // ── عنوان العميل في كل سطر (M15، ASVS 7.1.4) ──
        // كان النطاق يحمل "مَن" (UserId) و"أين" (TenantId) ولا يحمل "من أين" إطلاقاً — والعنوان كان
        // يُكتب في سطر التدقيق وحده. فحملةُ حشو بيانات اعتماد تظهر في السجلّات تيّاراً من
        // `POST /api/auth/login responded 401` لا يُميَّز عن مستخدمين نسوا كلماتهم: لا حساب مذكور،
        // ولا مصدر، فلا تجميع ولا إنذار ولا حجب. والقيمة هي التي صحّحها UseForwardedHeaders، أي عنوان
        // الزائر خلف وكيلٍ موثوق لا عنوان الوكيل.
        if (context.Connection.RemoteIpAddress is { } address)
            scope["ClientIp"] = address.ToString();
        if (tenancy.Tenant is { } tenant)
            scope["TenantId"] = tenant.Id;
        else if (tenancy.Scope == TenantScope.Platform)
            scope["Area"] = "Platform";
        if (currentUser.UserId is int userId)
            scope["UserId"] = userId;
        return scope;
    }
}
