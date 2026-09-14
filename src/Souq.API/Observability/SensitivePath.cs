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

    // احتياط لما قبل التوجيه. الطريقة أعلاه تقرأ RouteValues، وهي لا تُملأ إلا بعد UseRouting —
    // فاستثناء يقع في وسيط مبكّر (تحديد المستأجر مثلاً، حين تتعثّر القاعدة) يصل إلى معالج
    // الاستثناءات بمسار خام يحمل الرمز كاملاً. عندها نقنّح آخر مقطع من المسارات المعروف أنها
    // تنتهي بسرّ. القائمة تُراجَع مع كل نقطة جديدة تحمل سرّاً في مسارها.
    private static readonly string[] SecretTrailingSegmentPrefixes = ["/api/orders/track/"];

    // يعمل بعد التوجيه، فقيم المسار متاحة. لا مطابقة نصّية عمياء: تُستبدل قيمة المعامل الحسّاس نفسها وحدها، فيبقى
    // السطر مفيداً للتشخيص (الطريقة والقالب والرمز والمدّة).
    public static string Redact(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var redactedByRoute = false;
        foreach (var name in SensitiveRouteValues)
            if (context.Request.RouteValues.TryGetValue(name, out var value)
                && value is string secret && secret.Length > 0)
            {
                path = path.Replace(secret, Redacted, StringComparison.Ordinal);
                redactedByRoute = true;
            }

        return redactedByRoute ? path : RedactTrailingSecret(path);
    }

    // قبل التوجيه: نحذف ما بعد البادئة المعروفة بدل الاعتماد على قيم لم تُحسب بعد.
    private static string RedactTrailingSecret(string path)
    {
        foreach (var prefix in SecretTrailingSegmentPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.Length > prefix.Length)
                return prefix + Redacted;
        return path;
    }
}
