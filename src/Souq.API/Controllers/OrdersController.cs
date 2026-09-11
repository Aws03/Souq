using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Features.Orders.Queries;

namespace Souq.API.Controllers;

// كل نقاط الطلبات تتطلّب تسجيل الدخول. الهوية وفحص الملكية يعيشان في Application
// (ICurrentUser) — هذا الـ Controller لا يقرأ المطالبات ولا يقرّر من يرى ماذا (Phase 0 B7).
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrdersController(IMediator mediator) => _mediator = mediator;

    // POST /api/orders — إتمام الطلب والدفع. العميل هو المستخدم الحالي دائماً (من التوكن،
    // لا من الجسم). تعارض مخزون متزامن ⇒ 409 من المعالج المركزي.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.IsSuccess) return this.Failure(result);
        return CreatedAtAction(nameof(GetById), new { id = result.Value!.OrderId }, result.Value);
    }

    // POST /api/orders/5/confirm-payment — لصاحب الطلب أو مدير الطلبات فقط؛ غيرهما 404.
    [HttpPost("{id:int}/confirm-payment")]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        var result = await _mediator.Send(new ConfirmOrderPaymentCommand(id));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/orders/5 — يراه صاحبه أو مدير الطلبات؛ غيرهما 404 (لا نكشف الوجود).
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/orders/mine?page=1&pageSize=20 — طلبات المستخدم الحالي، مرقّمة.
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new GetMyOrdersQuery(page, pageSize)));

    // GET /api/orders/5/tracking — رابط تتبّع قابل للمشاركة بلا مصادقة عمداً. العقد
    // يكشف الحدّ الأدنى فقط (رموز تتبّع عشوائية بدل المعرّف في المرحلة 9).
    [HttpGet("{id:int}/tracking")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTracking(int id)
    {
        var result = await _mediator.Send(new GetOrderTrackingQuery(id));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/orders?customerId= — كل الطلبات مرقّمة لشاشة الإدارة، أو طلبات عميل واحد (صفحة تفاصيله).
    [HttpGet]
    [HasPermission(Permissions.Orders.View)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? customerId = null)
        => Ok(await _mediator.Send(new GetOrdersQuery(page, pageSize, customerId)));

    // PUT /api/orders/5/status — شحن/تسليم/إلغاء. الإلغاء يعيد المخزون المحجوز.
    [HttpPut("{id:int}/status")]
    [HasPermission(Permissions.Orders.Manage)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest body)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand(
            id, body.Action, body.Note, body.TrackingNumber, body.ShippingCarrier));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }
}

// جسم طلب تحديث الحالة. الإجراء enum يُرسَل كنص ("Ship"/"Deliver"/"Cancel").
public record UpdateOrderStatusRequest(
    OrderStatusAction Action, string? Note = null, string? TrackingNumber = null, string? ShippingCarrier = null);
