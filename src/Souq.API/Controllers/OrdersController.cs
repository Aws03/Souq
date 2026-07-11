using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Features.Orders.Queries;
using Souq.Domain.Common;

// ملاحظة: OrderStatusAction معرّف في Souq.Application.Features.Orders.Commands.

namespace Souq.API.Controllers;

// كل نقاط الطلبات تتطلّب تسجيل الدخول: لا طلب دون هوية معروفة.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrdersController(IMediator mediator) => _mediator = mediator;

    // POST /api/orders — إتمام الطلب والدفع.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderCommand command)
    {
        // مصدر الحقيقة لهوية العميل هو التوكن لا جسم الطلب — نتجاهل أي CustomerId
        // مدسوس في الجسم ونفرض هوية المستخدم المصادَق (منع انتحال الطلبات).
        var result = await _mediator.Send(command with { CustomerId = CurrentUserId() });
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.OrderId }, result.Value);
    }

    // GET /api/orders/5 — متابعة الطلب. يراه صاحبه أو المدير فقط.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id));
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });

        // فحص الملكية: غير المالك وغير المدير يُعامَل كأن الطلب غير موجود (404)
        // بدل 403 — كي لا نكشف وجود طلبات مستخدمين آخرين.
        if (result.Value!.CustomerId != CurrentUserId() && !User.IsInRole(Roles.Admin))
            return NotFound(new { error = "الطلب غير موجود" });

        return Ok(result.Value);
    }

    // GET /api/orders  (مدير) — كل الطلبات مرقّمة لشاشة الإدارة.
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new GetOrdersQuery(page, pageSize)));

    // PUT /api/orders/5/status  (مدير) — انتقال حالة الطلب (شحن/تسليم/إلغاء).
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest body)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand(id, body.Action));
        if (result.IsSuccess) return NoContent();
        return result.ErrorCode == "NotFound"
            ? NotFound(new { error = result.Error })
            : BadRequest(new { error = result.Error, code = result.ErrorCode });
    }

    private int CurrentUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

// جسم طلب تحديث الحالة. الإجراء enum يُرسَل كنص ("Ship"/"Deliver"/"Cancel")
// بفضل JsonStringEnumConverter المسجّل في Program.
public record UpdateOrderStatusRequest(OrderStatusAction Action);
