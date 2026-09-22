using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Souq.API.Http;
using Souq.API.Tenancy;

namespace Souq.API.Security;

// أسماء سياسات حدّ المعدّل كما تعلنها النقاط ([EnableRateLimiting]).
public static class RateLimitPolicies
{
    public const string Auth = "auth";                  // دخول، تسجيل، استعادة/تغيير كلمة المرور، تأكيد البريد
    public const string Refresh = "auth-refresh";       // تجديد الجلسة (يتكرّر كل 15 دقيقة لكل تبويب)
    public const string CouponPreview = "coupon-preview"; // تخمين رموز الكوبونات
    public const string Basket = "basket";              // كتابة السلة (كل إضافة من زائر جديد تُنشئ سلة)

    // ========================================================================
    // تصدير البيانات (F-21): نقطةٌ **ثقيلة** يستطيع أيّ عميل مسجَّل استدعاءها — تقرأ كل طلباته
    // بكل أسطرها وكل تقييماته في استجابة واحدة، وتكبر بعمر الحساب لا بصفحة يطلبها.
    //
    // والحدّ على **التكرار** لا على المحتوى، عن قصد: التصدير حقٌّ لصاحب البيانات، وتصديرٌ مبتور
    // ليس تصديراً — فقصُّه لتخفيف الحمل كان يكسر الغرض الذي وُجد له. ما يُقيَّد هو كم مرّة يُطلب.
    // ========================================================================
    public const string Export = "data-export";
}

// الحدود قابلة للضبط (RateLimiting:Auth:PermitLimit…) — الاختبارات ترفعها، والإنتاج يضيّقها إن لزم.
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public WindowLimit Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public WindowLimit Refresh { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public WindowLimit CouponPreview { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public WindowLimit Basket { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    // ستّ مرّات في الساعة: تنزيل نسخة ثم إعادة المحاولة بعد خطأ شبكة يسع فيها مراراً، وحلقةٌ
    // تستنزف القاعدة لا تسع.
    public WindowLimit Export { get; set; } = new() { PermitLimit = 6, WindowSeconds = 3600 };

    // ========================================================================
    // **عددُ النسخ التي تخدم هذه الحدود** (C4، [ADR-0057](0057)).
    //
    // نوافذُ الحدّ في الذاكرة، ولكلِّ نسخةٍ نوافذُها — فالحدُّ الفعليّ عبر موازِن حِملٍ يوزّع
    // بالتناوب هو **الحدُّ × عددُ النسخ**. وهذا ليس شيئاً تُصلحه إشارةُ إبطال: الإبطالُ يجعل
    // النسخَ تتفق على ما **قرأته**، والحدُّ عدّادٌ يجب أن **تتشاركه**، وتشاركُه يعني قراءةً
    // وكتابةً في مخزنٍ مشترك على مسارِ كلِّ طلب — وهو ما لا تصلح له قاعدةُ بيانات، والمخزنُ
    // الموزَّع غيرُ مقصودٍ صراحةً (ExplicitNonGoals §9).
    //
    // فالتقريبُ المتاح هو قسمةُ الحدّ على عدد النسخ، وهو ما يفعله هذا المفتاح. وهو **تقريبٌ لا
    // ضبط**: مع توزيعٍ متساوٍ يقارب المجموعُ الحدَّ المقصود، ومع توزيعٍ غير متساوٍ قد يُرفض
    // طلبٌ كان يجب أن يُقبل.
    //
    // والافتراضُ 1 — أي **لا تغيير في السلوك** لمن يشغّل نسخةً واحدة، وهو حالُ كلّ نشرٍ اليوم.
    // ========================================================================
    public int InstanceCount { get; set; } = 1;
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

            AddPolicy(limiter, RateLimitPolicies.Auth, options.Auth, options.InstanceCount);
            AddPolicy(limiter, RateLimitPolicies.Refresh, options.Refresh, options.InstanceCount);
            AddPolicy(limiter, RateLimitPolicies.CouponPreview, options.CouponPreview, options.InstanceCount);
            AddPolicy(limiter, RateLimitPolicies.Basket, options.Basket, options.InstanceCount);
            AddPolicy(limiter, RateLimitPolicies.Export, options.Export, options.InstanceCount);
        });
        return services;
    }

    // الحدُّ لكلِّ نسخة: المقصودُ مقسوماً على عددها، وبحدٍّ أدنى واحد — قسمةٌ تُنتج صفراً كانت
    // ستُغلق النقطة تماماً، وهو أسوأُ بكثير من حدٍّ أوسع ممّا قُصد.
    public static int PerInstance(int permitLimit, int instances) =>
        instances <= 1 ? permitLimit : Math.Max(1, permitLimit / instances);

    private static void AddPolicy(RateLimiterOptions limiter, string policy, WindowLimit limit, int instances) =>
        limiter.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            // المضيف بصيغته القانونية لا كما وصل (M15): `Request.Host.Host` يحفظ حالة الأحرف والنقطة
            // الأخيرة، وتحديد المتجر يُطبّعها — فمتجرٌ واحد كان له دلوٌ لكل صيغة، وتبديل حالة الأحرف
            // وحده كان يُلغي الحدّ تماماً. أُثبت على حزمة تعمل قبل الإصلاح، ويحرسه اختبار انحدار.
            $"{RequestHost.Canonical(context)}|{context.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PerInstance(limit.PermitLimit, instances),
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                QueueLimit = 0,
            }));
}
