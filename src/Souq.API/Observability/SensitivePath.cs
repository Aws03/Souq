namespace Souq.API.Observability;

// ============================================================================
// تنقيح قيم المسار الحسّاسة من أي سطر سجلّ يكتب المسار (R-10): من يحمل رمز تتبّع الطلب يفتح صفحة تتبّع صاحبه بلا
// مصادقة، والرمز جزء من المسار (/api/orders/track/{token}) — فمن يقرأ السجلّ يقرأ الرمز.
//
// المكان مشترك عمداً: أول إصلاح نقّح سطر الطلب وحده وبقي معالج الاستثناءات يكتب المسار خاماً، فكان الرمز يتسرّب من
// مسار الخطأ وحده. قاعدة واحدة في مكان واحد بدل نسختين تتباعدان.
// ============================================================================
public static class SensitivePath
{
    // قيم مسار سرّية بطبيعتها: من يحملها يفتح المورد بلا مصادقة.
    private static readonly string[] SensitiveRouteValues = ["token"];
    private const string Redacted = "***";

    // يعمل بعد التوجيه، فقيم المسار متاحة. لا مطابقة نصّية عمياء: تُستبدل قيمة المعامل الحسّاس نفسها وحدها، فيبقى
    // السطر مفيداً للتشخيص (الطريقة والقالب والرمز والمدّة).
    public static string Redact(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        foreach (var name in SensitiveRouteValues)
            if (context.Request.RouteValues.TryGetValue(name, out var value)
                && value is string secret && secret.Length > 0)
                path = path.Replace(secret, Redacted, StringComparison.Ordinal);
        return path;
    }
}
