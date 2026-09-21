using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Analytics;
using Souq.Application.Features.Analytics.Contracts;

namespace Souq.API.Analytics;

// ============================================================================
// معرّفا الزائر والجلسة: يُصكّان في الخادم، ويُحملان في ملفَّي تعريف ارتباط، ولا يُقرآن من العميل.
//
// **مُعتِمان تماماً.** عشوائيان من مُولِّد تعميةٍ، لا بريد ولا بصمة جهاز ولا معرّف إعلاني ولا
// مشتقٌّ من IP. لا معنى لهما خارج سوق، فلا قيمة لهما لأحدٍ آخر، ويُبدَّلان أو يُلغَيان بلا مسّ
// بقيّةِ المخطَّط ([ADR-0050](0050) §5، والتصميمُ الأقلّ كلفةً للرجوع في سجلّ `C-08`).
//
// **و`IsEssential = false` — وهو الفرق عن ملفّ السلة، لا سهو.** ملفُّ السلة لازمٌ لوظيفةٍ طلبها
// الزائر (سلّةٌ تبقى)، فهو ضروريّ. ومعرّفُ الزائر **ليس ضرورياً**: المتجر يعمل كاملاً بلا أن
// يُخزَّن. ولذلك يُعلَن غير ضروريّ، فتستطيع سياسةُ الموافقة كبتَه — وهو بالضبط موضعُ السؤال الذي
// أبقاه قرار المالك C-08 = A للمالك: على أيّ أساسٍ قانونيّ يُخزَّن، وأتُجمَع موافقةٌ لكل متجر أم
// مرّةً للمنصّة.
//
// **ولا يُكتب أيٌّ منهما والالتقاطُ معطّل.** فمتجرٌ لم يُفعِّل الالتقاط لا يضع على متصفّح زائره
// شيئاً على الإطلاق: لا معرّف بلا حدث، ولا حدث بلا إعداد.
//
// **وقاعدةُ الثلاثين دقيقة تُنفَّذ بانتهاء صلاحية الملفّ نفسه**، لا بطابعٍ يُقرأ ويُقارَن: ملفُّ
// الجلسة يُجدَّد مع كل طلب إلى «الآن + الخمول»، فمتصفّحٌ سكت أكثر منه يُسقطه، فتُصكّ جلسةٌ جديدة
// في الطلب التالي. لا حالةَ خادم، ولا طابعَ زمنٍ يستطيع العميل تحريكه.
// ============================================================================
internal static class VisitorCookies
{
    public const string VisitorBareName = "souq_v";
    public const string SessionBareName = "souq_s";

    // مسارٌ ضيّق حين يكون الملفّ غير آمن (تطوير محليّ): الأحداث تُلتقَط في طلبات الـ API وحدها.
    // ومع `__Host-` يصير المسار "/" إلزاماً — وهو شرط البادئة نفسها (HostOnlyCookie).
    public const string NarrowPath = "/api";

    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    public static string VisitorName(bool secure) => HostOnlyCookie.NameFor(VisitorBareName, secure);
    public static string SessionName(bool secure) => HostOnlyCookie.NameFor(SessionBareName, secure);

    public static CookieOptions Options(bool secure, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = HostOnlyCookie.PathFor(NarrowPath, secure),
        Expires = expires,
        // القياس ليس وظيفةً طلبها الزائر. انظر رأس الملفّ.
        IsEssential = false,
    };
}

// ============================================================================
// سياقُ الزائر لهذا الطلب. يُضبَط مرّةً في الوسيط ويُقرأ في المصرف — والحالات لا تبنيه.
// ============================================================================
internal sealed class RequestVisitorContext : IVisitorContext
{
    public string? VisitorId { get; private set; }
    public string? SessionId { get; private set; }
    public Guid? SearchExecutionId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string Surface { get; private set; } = BehaviouralSurfaceNames.Storefront;

    internal void Use(string? visitorId, string? sessionId, Guid? searchExecutionId, string? correlationId, string surface)
    {
        VisitorId = visitorId;
        SessionId = sessionId;
        SearchExecutionId = searchExecutionId;
        CorrelationId = correlationId;
        Surface = surface;
    }

    // معرّفُ تنفيذ البحث يُصكّ داخل الطلب (معالج البحث) ثم يُردّ مع النتائج، فالسياق يحمله كي
    // يلتقطه حدثُ البحث نفسه بلا أن يمرّره كلُّ مُنادٍ.
    internal void UseSearchExecution(Guid searchExecutionId) => SearchExecutionId = searchExecutionId;
}

// أسماءُ الأسطح كما يراها الـ API. نسخةٌ من `BehaviouralSurfaces` في المجال، وتطابقُهما محروسٌ
// باختبار: سطحٌ لا يعرفه المجال يُسقِط كلَّ حدثٍ يحمله بصمت.
internal static class BehaviouralSurfaceNames
{
    public const string Storefront = "storefront";
    public const string Admin = "admin";
    public const string Platform = "platform";
}

// ============================================================================
// الوسيط: يقرأ الملفَّين، يصكّ ما ينقص، ويضبط سياق الطلب — ولا يفعل شيئاً إن كان الالتقاط معطّلاً.
//
// موضعُه في الخطّ **بعد تحديد المتجر وقبل المصادقة**: يحتاج المتجر (لا معرّف لزائرٍ بلا متجر)
// ولا يحتاج هويّةَ المستخدم (الزائر مُعتِم بالتعريف، ولا يُشتقّ من حساب).
// ============================================================================
internal sealed class VisitorCookieMiddleware
{
    // ترويسةُ ردّ معرّف تنفيذ البحث من العميل. لا تُقبَل إلا GUID: نصٌّ حرٌّ من العميل لا يدخل
    // عموداً مفهرساً.
    public const string SearchExecutionHeader = "X-Souq-Search";

    private readonly RequestDelegate _next;

    public VisitorCookieMiddleware(RequestDelegate next) => _next = next;

    // الوقت من `TimeProvider` لا من `DateTimeOffset.UtcNow`: مدّةُ صلاحية ملفّ الجلسة **هي** قاعدة
    // الثلاثين دقيقة، فاختبارُها يحتاج ساعةً يمكن تحريكها.
    public async Task InvokeAsync(
        HttpContext context, ITenantContext tenancy, RequestVisitorContext visitor,
        EventCaptureSettings settings, IOptions<RefreshCookieOptions> cookie, TimeProvider clock)
    {
        var surface = Surface(context, tenancy);
        var correlation = context.TraceIdentifier;
        var searchExecution = SearchExecution(context);

        // الالتقاط معطّلاً: سياقٌ بلا معرّفات، ولا ملفّ يُوضع على متصفّح أحد.
        if (!settings.CaptureIsConfigured || !settings.VisitorIdentifierEnabled || tenancy.Scope != TenantScope.Tenant)
        {
            visitor.Use(null, null, searchExecution, correlation, surface);
            await _next(context);
            return;
        }

        var secure = cookie.Value.Secure;
        var visitorName = VisitorCookies.VisitorName(secure);
        var sessionName = VisitorCookies.SessionName(secure);

        var visitorId = Existing(context, visitorName) ?? VisitorCookies.New();
        var sessionId = Existing(context, sessionName) ?? VisitorCookies.New();

        // كلاهما يُجدَّد مع كل طلب: الزائر بمدّة الحفظ المُعلَنة (لا تُخترَع مدّةٌ هنا — هي جواب
        // المالك)، والجلسة بنافذة الخمول، فسكوتٌ أطول منها يُنهيها في المتصفّح نفسه.
        var now = clock.GetUtcNow();
        context.Response.Cookies.Append(visitorName, visitorId,
            VisitorCookies.Options(secure, now.AddDays(settings.RetentionDays)));
        context.Response.Cookies.Append(sessionName, sessionId,
            VisitorCookies.Options(secure, now.Add(settings.SessionIdle)));

        visitor.Use(visitorId, sessionId, searchExecution, correlation, surface);
        await _next(context);
    }

    // قيمةٌ لا تشبه ما نصكّه تُهمَل: ملفٌّ حقنه أحدٌ بقيمةٍ طويلة أو بمحارف غريبة لا يصير معرّفاً.
    private static string? Existing(HttpContext context, string name)
    {
        var value = context.Request.Cookies[name];
        if (string.IsNullOrEmpty(value) || value.Length > 64) return null;
        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? value : null;
    }

    private static Guid? SearchExecution(HttpContext context) =>
        Guid.TryParse(context.Request.Headers[SearchExecutionHeader].FirstOrDefault(), out var parsed) && parsed != Guid.Empty
            ? parsed
            : null;

    private static string Surface(HttpContext context, ITenantContext tenancy)
    {
        if (tenancy.Scope == TenantScope.Platform) return BehaviouralSurfaceNames.Platform;
        var path = context.Request.Path.Value ?? "";
        return path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase)
            ? BehaviouralSurfaceNames.Admin
            : BehaviouralSurfaceNames.Storefront;
    }
}
