using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.Application.Features.Auth.Commands;

namespace Souq.API.Controllers;

// نقاط المصادقة: مفتوحة للعموم (لا يملك الزائر توكناً بعد). التسجيل والدخول يُعيدان
// AuthResponse (توكن + بيانات المستخدم) عند النجاح، أو ProblemDetails موحّداً عند الفشل:
// بريد مكرّر ⇒ 409، بيانات دخول خاطئة ⇒ 401 (نوع الخطأ يقرّر، لا هذا الـ Controller).
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // مقصود: من لم يسجّل الدخول بعد لا يملك توكناً.
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // POST /api/auth/forgot-password — ينجح دائماً (200) بلا كشف إن كان البريد
    // مسجّلاً؛ التفصيل الفعلي (هل أُرسلت رسالة؟) يبقى في السجل الخادم فقط.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command)
    {
        await _mediator.Send(command);
        return Ok(new { message = "إن كان البريد الإلكتروني مسجّلاً، وصلك رابط إعادة التعيين." });
    }

    // POST /api/auth/reset-password — رمز غير صالح أو منتهٍ ⇒ 422 برسالة واضحة.
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? Ok(new { message = "تم تحديث كلمة المرور بنجاح." }) : this.Failure(result);
    }
}
