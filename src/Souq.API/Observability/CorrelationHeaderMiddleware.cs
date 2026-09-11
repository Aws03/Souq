using Souq.API.Http;

namespace Souq.API.Observability;

// ============================================================================
// يضيف X-Correlation-Id لكل استجابة — نجاحاً وخطأً (مسجَّل قبل معالج الاستثناءات) — بالقيمة
// نفسها في traceId بجسم الخطأ وفي نطاق سجلّ الطلب (ADR-0018). يطلبها الدعم من المستخدم
// فيجد طلبه في السجلات مباشرة، دون أن يقبل الخادم معرّفاً حرّاً من العميل.
// ============================================================================
public sealed class CorrelationHeaderMiddleware
{
    private readonly RequestDelegate _next;
    public CorrelationHeaderMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var correlationId = RequestCorrelation.GetId(context);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[RequestCorrelation.HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        return _next(context);
    }
}
