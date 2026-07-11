using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Common.Models;
using Souq.Application.Features.Auth;
using Souq.Application.Features.Auth.Commands;

namespace Souq.API.Controllers;

// نقاط المصادقة: مفتوحة للعموم (لا يملك الزائر توكناً بعد). كلاهما يُعيد
// AuthResponse (توكن + بيانات المستخدم) عند النجاح، أو خطأً موحّداً عند الفشل.
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command)
        => Respond(await _mediator.Send(command));

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command)
        => Respond(await _mediator.Send(command));

    // فشل بريد مكرّر ⇒ 409، بيانات دخول خاطئة ⇒ 401، غير ذلك ⇒ 400.
    private IActionResult Respond(Result<AuthResponse> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorCode switch
        {
            "EmailTaken"         => Conflict(new { error = result.Error, code = result.ErrorCode }),
            "InvalidCredentials" => Unauthorized(new { error = result.Error, code = result.ErrorCode }),
            _                    => BadRequest(new { error = result.Error, code = result.ErrorCode }),
        };
    }
}
