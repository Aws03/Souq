using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Models;
using Souq.Application.Features.Auth;
using Souq.Application.Features.Auth.Commands;
using Souq.Application.Features.Auth.Queries;

namespace Souq.API.Controllers;

// ============================================================================
// نقاط المصادقة (ADR-0010). على مضيف المتجر لحسابات ذلك المتجر، وعلى مضيف المنصّة لحساباتها — السلوك
// يتبع النطاق لا المسار. الجلسة جزءان:
//   توكن وصول قصير (15 د) — في جسم الاستجابة؛ تحفظه الواجهة في الذاكرة فقط (لا localStorage).
//   رمز تجديد دوّار — في ملف تعريف ارتباط HttpOnly/Secure/SameSite=Strict على مسار /api/auth وحده:
//   لا يقرؤه JavaScript (XSS لا يسرقه)، ولا يُرسَل لطلب من موقع آخر (CSRF)، ومضيف كل متجر له ملفه.
// حدّ المعدّل على كل ما يقبل كلمة مرور أو يرسل بريداً.
// ============================================================================
[ApiController]
[Route("api/[controller]")]
[AvailableOnAllHosts]
[AvailableDuringProvisioning] // إدارة متجر قيد التجهيز تسجّل الدخول لتجهيزه قبل الافتتاح.
public class AuthController : ControllerBase
{
    // الاسم العاري: ما يُبنى منه الاسم الفعلي. البادئة `__Host-` تُضاف حين يكون الملفّ مشفَّراً — وهي
    // ما يمنع مضيفاً شقيقاً تحت نطاق التاجر من زرع جلسةٍ لنا (انظر `HostOnlyCookie`).
    public const string RefreshCookieBareName = "souq_refresh";
    private const string RefreshCookieNarrowPath = "/api/auth";

    private string RefreshCookieName => HostOnlyCookie.NameFor(RefreshCookieBareName, _cookie.Secure);

    private readonly IMediator _mediator;
    private readonly RefreshCookieOptions _cookie;

    public AuthController(IMediator mediator, IOptions<RefreshCookieOptions> cookie)
    {
        _mediator = mediator; _cookie = cookie.Value;
    }

    // POST /api/auth/register — عميل جديد في متجر المضيف (لا تسجيل ذاتي على مضيف المنصّة: 403).
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command) =>
        Session(await _mediator.Send(command));

    // POST /api/auth/login — خطأ الاعتماد ⇒ 401 InvalidCredentials؛ مقفل ⇒ 401 AccountLocked.
    // متاحة والمتجر مغلق: إدارة متجر موقوف تدخل لترى حالته وتتواصل مع المنصّة — متجر موقوف كان يردّ 503 حتى على الدخول،
    // فيصير صفحةً بيضاء لصاحبه كما لزائره (R-08). ما وراء الدخول يبقى مغلقاً: نقاط الإدارة محروسة بصلاحية، والصلاحية
    // لا تفتح متجراً موقوفاً (TenantAvailabilityMiddleware.IsOpen).
    [HttpPost("login")]
    [AllowAnonymous]
    [AvailableWhenStoreClosed]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command) =>
        Session(await _mediator.Send(command));

    // POST /api/auth/refresh — الرمز من ملف تعريف الارتباط فقط؛ يُدوَّر مع كل تجديد. متاحة والمتجر مغلق كي لا تنقطع جلسة
    // إدارته بعد دقائق من إيقافه.
    [HttpPost("refresh")]
    [AllowAnonymous]
    [AvailableWhenStoreClosed]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<IActionResult> Refresh()
    {
        var result = await _mediator.Send(new RefreshSessionCommand(Request.Cookies[RefreshCookieName]));
        if (!result.IsSuccess) ClearRefreshCookie();
        return Session(result);
    }

    // POST /api/auth/logout — يُبطل جلسة هذا الجهاز ويمسح ملف تعريف الارتباط. 204 دائماً. متاحة والمتجر مغلق: الخروج
    // إجراء أمان لا يجوز حجبه.
    [HttpPost("logout")]
    [AllowAnonymous]
    [AvailableWhenStoreClosed]
    public async Task<IActionResult> Logout()
    {
        await _mediator.Send(new LogoutCommand(Request.Cookies[RefreshCookieName]));
        ClearRefreshCookie();
        return NoContent();
    }

    // GET /api/auth/me — الحساب الحالي كما هو الآن (الدور، الصلاحيات، تأكيد البريد). متاحة والمتجر مغلق كي تعرف الواجهة
    // من الداخل بعد دخوله.
    [HttpGet("me")]
    [Authorize]
    [AvailableWhenStoreClosed]
    public async Task<IActionResult> Me()
    {
        var result = await _mediator.Send(new GetCurrentUserQuery());
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // POST /api/auth/change-password — يُبطل كل الجلسات الأخرى ويعيد جلسة جديدة لهذا الجهاز.
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command) =>
        Session(await _mediator.Send(command));

    // POST /api/auth/forgot-password — ينجح دائماً (200) بلا كشف إن كان البريد مسجّلاً.
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "إن كان البريد الإلكتروني مسجّلاً، وصلك رابط إعادة التعيين." });
    }

    // POST /api/auth/reset-password — رمز غير صالح أو منتهٍ ⇒ 422 برسالة واضحة.
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? Ok(new { message = "تم تحديث كلمة المرور بنجاح." }) : this.Failure(result);
    }

    // POST /api/auth/verify-email — تأكيد البريد برمز الرسالة.
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // POST /api/auth/resend-verification — رابط تأكيد جديد للحساب الحالي.
    [HttpPost("resend-verification")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResendVerification()
    {
        var result = await _mediator.Send(new ResendVerificationCommand());
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // الجسم يحمل توكن الوصول وبيانات الحساب؛ رمز التجديد في ملف تعريف الارتباط وحده.
    private IActionResult Session(Result<AuthSession> result)
    {
        if (!result.IsSuccess) return this.Failure(result);

        var session = result.Value!;
        Response.Cookies.Append(RefreshCookieName, session.RefreshToken, CookieOptions(session.RefreshExpiresAt));
        return Ok(session.Response);
    }

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookieName, CookieOptions(expires: null));

    private CookieOptions CookieOptions(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = _cookie.Secure,
        SameSite = SameSiteMode.Strict,
        Path = HostOnlyCookie.PathFor(RefreshCookieNarrowPath, _cookie.Secure),
        Expires = expires,
        IsEssential = true,
    };
}
