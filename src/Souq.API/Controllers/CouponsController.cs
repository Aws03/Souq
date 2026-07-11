using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Common.Models;
using Souq.Application.Features.Coupons.Commands;
using Souq.Application.Features.Coupons.Queries;
using Souq.Domain.Common;

namespace Souq.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CouponsController : ControllerBase
{
    private readonly IMediator _mediator;
    public CouponsController(IMediator mediator) => _mediator = mediator;

    // GET /api/coupons/apply?code=SAVE10&subtotal=59.9 — عام: معاينة خصم قبل الدفع.
    [HttpGet("apply")]
    public async Task<IActionResult> Apply([FromQuery] string code, [FromQuery] decimal subtotal, [FromQuery] string currency = "JOD")
    {
        var result = await _mediator.Send(new ApplyCouponQuery(code, subtotal, currency));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error, code = result.ErrorCode });
    }

    // GET /api/coupons  (مدير) — كل الكوبونات مرقّمة (نشطة ومعطّلة معاً).
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new GetCouponsQuery(page, pageSize)));

    // POST /api/coupons  (مدير) — ينشئ كوبوناً.
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateCouponCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return StatusCode(StatusCodes.Status201Created, new { id = result.Value });
    }

    // PUT /api/coupons/5  (مدير) — يفرض معرّف المسار على الأمر.
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCouponCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : MapFailure(result);
    }

    // DELETE /api/coupons/5  (مدير) — حذف فعلي (آمن: لا مرجع أجنبي إليه، انظر تعليق DeleteCouponCommand).
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteCouponCommand(id));
        return result.IsSuccess ? NoContent() : MapFailure(result);
    }

    private IActionResult MapFailure(Result result) =>
        result.ErrorCode == "NotFound"
            ? NotFound(new { error = result.Error })
            : BadRequest(new { error = result.Error, code = result.ErrorCode });
}
