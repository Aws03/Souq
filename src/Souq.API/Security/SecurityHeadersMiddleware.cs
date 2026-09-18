namespace Souq.API.Security;

// ============================================================================
// ترويسات الأمان على كل استجابة — نجاحاً وخطأً (OnStarting، كما في CorrelationHeaderMiddleware:
// معالج الاستثناءات يعيد بناء الاستجابة، وترويسة تُضبط مباشرةً قد تضيع معها).
//
// هذه واجهة JSON لا صفحة: لا سكربت ولا نموذج ولا إطار، فالسياسة الصحيحة لها أضيق بكثير من
// سياسة تطبيق ويب — default-src 'none' تعني "لا شيء يُحمَّل من هذه الاستجابة إطلاقاً".
// ترويسات الواجهة نفسها (SPA) مكانها nginx: تلك صفحة حقيقية تحتاج سكربتاتها وأنماطها.
//
// ملاحظة ترتيب مهمة: /uploads يضبط سياسته الأشدّ (default-src 'none'; sandbox) في
// OnPrepareResponse، وهي تعمل *قبل* OnStarting — فالكتابة العمياء هنا كانت ستستبدل سياسة
// الملفات المرفوعة بأضعف منها بصمت. لذلك لا نلمس سياسة موجودة.
// ============================================================================
public sealed class SecurityHeadersMiddleware
{
    // لا شيء يُحمَّل، ولا تأطير، ولا إرسال نموذج من استجابة واجهة برمجية.
    private const string ApiContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    // ميزات المتصفّح التي لا تحتاجها أي استجابة من هذه الواجهة.
    private const string PermissionsPolicy =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        // Swagger صفحة حقيقية بسكربتات (Development وحده): سياسة الواجهة البرمجية تكسرها.
        var isSwagger = context.Request.Path.StartsWithSegments("/swagger");

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";      // لا تخمين نوع المحتوى
            headers["Referrer-Policy"] = "no-referrer";         // لا تسريب مسارات تحمل معرّفات
            headers["X-Frame-Options"] = "DENY";                // للمتصفّحات القديمة قبل frame-ancestors
            headers["Permissions-Policy"] = PermissionsPolicy;

            // ── لا تخزين مؤقّت لأي ردّ من الواجهة البرمجية (M15، ASVS 8.2.1) ──
            // كل ما تُعيده هذه الواجهة إمّا خاصّ بحسابٍ أو بمتجر: `/api/auth/me`، `/api/orders/mine`،
            // `/api/account/addresses`، و`/api/account/export` الذي يُعيد **كل** بيانات العميل ملفّاً
            // يُنزَّل. بلا توجيهٍ صريح يقرّر المتصفّح بنفسه، فيبقى الملفّ في ذاكرة القرص وفي مجلّد
            // التنزيلات بعد الخروج — وتنظيفُ الواجهة لذاكرتها (resetQueries) لا يمسّ ذاكرة HTTP.
            // وهو **افتراضٌ لا فرض**: نقطةٌ تضبط التوجيه بنفسها تُترك وشأنها — نفس قاعدة سياسة المحتوى
            // أسفله، ولنفس السبب. و`/api/storefront/config` هي الحالة: بيانُ عرضٍ عامّ لا بيانات
            // شخصية، وتضبط `no-cache` مع ETag عن قصد كي يتحقّق المتصفّح بـ If-None-Match ويأخذ 304 —
            // و`no-store` كان سيُبطل ذلك التصميم صامتاً (وهو ما أسقط اختباره فعلاً قبل هذا السطر).
            // فالافتراضي يحمي كل ما لم يُفكَّر فيه، والاستثناء يبقى قراراً مكتوباً في موضعه.
            if (!isSwagger && !headers.ContainsKey("Cache-Control"))
                headers["Cache-Control"] = "no-store";

            if (!isSwagger && !headers.ContainsKey("Content-Security-Policy"))
                headers["Content-Security-Policy"] = ApiContentSecurityPolicy;

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
