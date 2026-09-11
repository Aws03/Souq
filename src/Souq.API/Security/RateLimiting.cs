using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Souq.API.Http;

namespace Souq.API.Security;

// أسماء سياسات حدّ المعدّل كما تعلنها النقاط ([EnableRateLimiting]).
public static class RateLimitPolicies
{
    public const string Auth = "auth";                  // دخول، تسجيل، استعادة/تغيير كلمة المرور، تأكيد البريد
    public const string Refresh = "auth-refresh";       // تجديد الجلسة (يتكرّر كل 15 دقيقة لكل تبويب)
    public const string CouponPreview = "coupon-preview"; // تخمين رموز الكوبونات
    public const string Basket = "basket";              // كتابة السلة (كل إضافة من زائر جديد تُنشئ سلة)
}

// الحدود قابلة للضبط (RateLimiting:Auth:PermitLimit…) — الاختبارات ترفعها، والإنتاج يضيّقها إن لزم.
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public WindowLimit Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public WindowLimit Refresh { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public WindowLimit CouponPreview { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public WindowLimit Basket { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };
}

public sealed class WindowLimit
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

// ملف تعريف ارتباط رمز التجديد: Secure افتراضياً. تعطيله مسموح فقط لنشر محلي على http بعنوان غير
// localhost (المتصفّحات تعامل localhost و*.localhost كسياق آمن حتى على http).
public sealed class RefreshCookieOptions
{
    public const string SectionName = "Auth:RefreshCookie";

    public bool Secure { get; set; } = true;
}

// ============================================================================
// حدّ المعدّل (Security.md §4، Phase 0 B5) بالمحدِّد المدمج في ASP.NET Core: نافذة ثابتة لكل
// (مضيف، عنوان عميل) — متجر مزدحم لا يستنفد حدّ متجر آخر، ومهاجم واحد لا يخنق الجميع. خلف Nginx
// يُقرأ العنوان من X-Forwarded-For فقط من وكيل موثوق (ForwardedHeaders في Program). الرفض 429
// ProblemDetails بعقد الأخطاء نفسه + Retry-After.
// ============================================================================
public static class RateLimitingSetup
{
    public static IServiceCollection AddSouqRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                await ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
                    "TooManyRequests", "محاولات كثيرة. انتظر قليلاً ثم حاول مجدداً.");
            };

            AddPolicy(limiter, RateLimitPolicies.Auth, options.Auth);
            AddPolicy(limiter, RateLimitPolicies.Refresh, options.Refresh);
            AddPolicy(limiter, RateLimitPolicies.CouponPreview, options.CouponPreview);
            AddPolicy(limiter, RateLimitPolicies.Basket, options.Basket);
        });
        return services;
    }

    private static void AddPolicy(RateLimiterOptions limiter, string policy, WindowLimit limit) =>
        limiter.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Request.Host.Host}|{context.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
            }));
}
