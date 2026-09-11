using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Coupons.Commands;
using Souq.Application.Features.Coupons.Queries;

namespace Souq.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CouponsController : ControllerBase
{
    private readonly IMediator _mediator;
    public CouponsController(IMediator mediator) => _mediator = mediator;

    // GET /api/coupons/apply?code=SAVE10&subtotal=59.9 — عام: معاينة خصم قبل الدفع، بعملة المتجر
    // دائماً (عملة يرسلها العميل لم تعد تُقبل — Phase 2).
    [HttpGet("apply")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.CouponPreview)]   // تخمين الرموز بالقوة الغاشمة
    public async Task<IActionResult> Apply([FromQuery] string code, [FromQuery] decimal subtotal)
    {
        var result = await _mediator.Send(new ApplyCouponQuery(code, subtotal));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/coupons — كل الكوبونات مرقّمة (نشطة ومعطّلة معاً).
    [HttpGet]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new GetCouponsQuery(page, pageSize)));

    // POST /api/coupons — ينشئ كوبوناً.
    [HttpPost]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateCouponCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, new { id = result.Value }) : this.Failure(result);
    }

    // PUT /api/coupons/5 — يفرض معرّف المسار على الأمر.
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCouponCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // DELETE /api/coupons/5 — حذف فعلي (الطلبات تحمل لقطة نصّية من الرمز فقط).
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteCouponCommand(id));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }
}
