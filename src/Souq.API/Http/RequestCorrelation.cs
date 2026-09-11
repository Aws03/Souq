using System.Diagnostics;

namespace Souq.API.Http;

// ============================================================================
// معرّف ربط الطلب (Correlation Id) = W3C TraceId لنشاط الطلب الحالي. ASP.NET Core
// ينشئ النشاط لكل طلب ويحترم ترويسة traceparent الواردة (بوّابة/خدمة سابقة)، والسجلات
// تحمل TraceId نفسه في نطاقها تلقائياً — فالقيمة واحدة في: السجل، وtraceId بجسم الخطأ،
// وترويسة الاستجابة. لا نقبل معرّفاً حرّاً من العميل (نص يكتبه أي أحد في سجلاتنا).
// ============================================================================
public static class RequestCorrelation
{
    public const string HeaderName = "X-Correlation-Id";

    public static string GetId(HttpContext context)
    {
        var activity = Activity.Current;
        return activity is not null && activity.TraceId != default
            ? activity.TraceId.ToHexString()
            : context.TraceIdentifier;
    }
}
