namespace Souq.API.Observability;

// ============================================================================
// يكشف أخطر خطأ إعداد صامت في هذا النشر: وكيل غير موثوق.
//
// UseForwardedHeaders لا يصدّق X-Forwarded-* إلا من شبكة مذكورة في ForwardedHeaders:KnownNetworks.
// وافتراضي الإطار حلقة محلية فقط — بينما nginx في Docker على 172.x، وفي Kubernetes على شبكة أخرى.
// فإن نُسي المفتاح لم يحدث خطأ ولا رسالة: الترويسات تُتجاهَل بصمت، وثلاثة أشياء تنكسر معاً —
//   • المخطّط يبقى http، فلا تُرسَل HSTS أبداً، وروابط البريد تخرج http://،
//   • وعنوان العميل يصير عنوان الوكيل، فيتشارك كل الزوّار دلو حدّ المعدّل نفسه
//     (وزائر واحد يستنفده للجميع)،
//   • وسطر السجلّ ينسب كل طلب إلى الوكيل.
//
// الكشف دقيق لا تخميني: وسيط الإطار *يحذف* القيمة التي استهلكها من الترويسة. فبقاء
// X-Forwarded-Proto بعده مع مخطّط http يعني بالضبط "وصلت الترويسة ولم تُصدَّق".
// يُسجَّل مرّة واحدة لكل عملية: تحذير يتكرّر مع كل طلب يُطفَأ، لا يُقرأ.
//
// ── وسببٌ ثانٍ لنفس الأعراض، أُضيف في M15: سلسلة أطول من الحدّ ──
// ForwardLimit افتراضه **واحد**، وDeployment.md يوصي بإنهاء TLS عند الحافّة — أي أمام nginx. وnginx
// يُلحق ولا يستبدل، فتصل X-Forwarded-For بقيمتين ("الزائر، المُنهي")، ويُستهلك الأيمن وحده: فيصير عنوان
// كل زائر هو عنوان المُنهي. نفس ثلاثة الأعراض أعلاه بالضبط، وبسبب مختلف تماماً — ولا يكشفه الفحص الأول،
// لأنّ X-Forwarded-Proto تصل بقيمة واحدة (nginx يستبدلها) فتُستهلك بنجاح ولا يبقى منها شيء.
//
// والكشف هنا دقيق بنفس القدر ولنفس السبب: بقاء قيمةٍ في X-Forwarded-For بعد الوسيط يعني بالضبط
// "السلسلة أطول ممّا استُهلك"، أي أنّ RemoteIpAddress وكيلٌ لا زائر.
// ============================================================================
public sealed class ProxyTrustDiagnostics
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProxyTrustDiagnostics> _logger;
    private int _reported;

    public ProxyTrustDiagnostics(RequestDelegate next, ILogger<ProxyTrustDiagnostics> logger)
    {
        _next = next; _logger = logger;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (Volatile.Read(ref _reported) == 0 && Warning(context) is { } warning
            && Interlocked.Exchange(ref _reported, 1) == 0)
            _logger.LogWarning("Configuration warning: {ConfigurationWarning}", warning);

        return _next(context);
    }

    // السببان يُفحصان بترتيبهما: "وكيل غير موثوق" يمنع تصديق الترويسات أصلاً، فلا معنى لفحص طول السلسلة
    // بعده. ورسالةٌ واحدة تُسجَّل لا اثنتان: التشخيص الثاني يصير صحيحاً غالباً بعد إصلاح الأول.
    private static string? Warning(HttpContext context)
    {
        if (WasForwardedProtoIgnored(context))
            return $"وصلت X-Forwarded-Proto من {context.Connection.RemoteIpAddress} ولم تُصدَّق: هذا العنوان ليس في "
                + "ForwardedHeaders:KnownNetworks. النتيجة: لا HSTS، وروابط بريد بـ http، وحدّ معدّل مشترك بين كل "
                + "الزوّار. اضبط الشبكة التي يأتي منها الوكيل (TRUSTED_PROXY_NETWORKS في حزمة Compose).";

        if (HasUnconsumedForwardedFor(context))
            return $"بقيت قيم في X-Forwarded-For بعد معالجتها: {context.Request.Headers["X-Forwarded-For"]}. "
                + $"السلسلة أطول من ForwardedHeaders:ForwardLimit، فـ {context.Connection.RemoteIpAddress} عنوان "
                + "وكيلٍ لا عنوان زائر. النتيجة: حدّ معدّل واحد يتشاركه كل الزوّار، وكل سطر تدقيق منسوب إلى "
                + "الوكيل. اضبط ForwardedHeaders:ForwardLimit على عدد الوكلاء بين الزائر والـ API "
                + "(وكيلان مثلاً إن كان TLS يُنهى أمام nginx).";

        return null;
    }

    // بعد UseForwardedHeaders: الترويسة باقية + المخطّط http ⇒ لم تُصدَّق. عامّة كي تُختبر
    // مباشرةً: إعادة إنتاج "وكيل غير موثوق" داخل TestServer غير ممكنة — عنوان العميل هناك null
    // فيُطبّق الإطار الترويسة ويستهلكها، وهي حالة مختلفة تماماً عن الحالة محلّ الفحص.
    public static bool WasForwardedProtoIgnored(HttpContext context) =>
        !context.Request.IsHttps
        && context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto)
        && proto.Count > 0
        && proto.ToString().Contains("https", StringComparison.OrdinalIgnoreCase);

    // بعد UseForwardedHeaders: بقاء أي قيمة في X-Forwarded-For ⇒ السلسلة أطول من الحدّ. عامّة لتُختبر
    // مباشرةً، كأختها.
    public static bool HasUnconsumedForwardedFor(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded)
        && !string.IsNullOrWhiteSpace(forwarded.ToString());
}
