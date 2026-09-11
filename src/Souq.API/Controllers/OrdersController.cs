using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.Application.Common.Models;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Features.Orders.Queries;
using Souq.Domain.Common;

namespace Souq.API.Controllers;

// كل نقاط الطلبات تتطلّب تسجيل الدخول: لا طلب دون هوية معروفة.
// (فحص الملكية ما زال هنا مؤقتاً — ينتقل إلى Application مع ICurrentUser في المرحلة 1B.)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrdersController(IMediator mediator) => _mediator = mediator;

    // POST /api/orders — إتمام الطلب والدفع. هوية العميل من التوكن لا من الجسم
    // (منع انتحال الطلبات). تعارض مخزون متزامن ⇒ 409 من الوسيط المركزي.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.Send(command with { CustomerId = CurrentUserId() });
        if (!result.IsSuccess) return this.Failure(result);
        return CreatedAtAction(nameof(GetById), new { id = result.Value!.OrderId }, result.Value);
    }

    // POST /api/orders/5/confirm-payment — بعد مصادقة العميل على الدفع مع Stripe من
    // متصفّحه؛ نتحقّق من النتيجة لدى البوّابة نفسها. الجسم يحمل الحالة الفعلية.
    [HttpPost("{id:int}/confirm-payment")]
    public async Task<IActionResult> ConfirmPayment(int id)
    {
        // فحص الملكية أولاً: لا يؤكّد عميل دفع طلب عميل آخر.
        var order = await _mediator.Send(new GetOrderByIdQuery(id));
        if (!order.IsSuccess) return this.Failure(order);
        if (order.Value!.CustomerId != CurrentUserId() && !User.IsInRole(Roles.Admin))
            return this.Failure(Error.NotFound("الطلب غير موجود"));

        var result = await _mediator.Send(new ConfirmOrderPaymentCommand(id));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/orders/5 — يراه صاحبه أو المدير فقط؛ غيرهما يُعامَل كأن الطلب غير موجود
    // (404 لا 403 — لا نكشف وجود طلبات مستخدمين آخرين).
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id));
        if (!result.IsSuccess) return this.Failure(result);
        if (result.Value!.CustomerId != CurrentUserId() && !User.IsInRole(Roles.Admin))
            return this.Failure(Error.NotFound("الطلب غير موجود"));
        return Ok(result.Value);
    }

    // GET /api/orders/mine — طلبات العميل الحالي. الهوية من التوكن حصراً.
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
        => Ok(await _mediator.Send(new GetMyOrdersQuery(CurrentUserId())));

    // GET /api/orders/5/tracking — رابط تتبّع قابل للمشاركة بلا مصادقة عمداً. العقد
    // يكشف الحدّ الأدنى فقط (رموز تتبّع عشوائية بدل المعرّف في المرحلة 9).
    [HttpGet("{id:int}/tracking")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTracking(int id)
    {
        var result = await _mediator.Send(new GetOrderTrackingQuery(id));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/orders  (مدير) — كل الطلبات مرقّمة لشاشة الإدارة.
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new GetOrdersQuery(page, pageSize)));

    // PUT /api/orders/5/status  (مدير) — شحن/تسليم/إلغاء. الإلغاء يعيد المخزون المحجوز.
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest body)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand(
            id, body.Action, body.Note, body.TrackingNumber, body.ShippingCarrier));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    private int CurrentUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

// جسم طلب تحديث الحالة. الإجراء enum يُرسَل كنص ("Ship"/"Deliver"/"Cancel").
public record UpdateOrderStatusRequest(
    OrderStatusAction Action, string? Note = null, string? TrackingNumber = null, string? ShippingCarrier = null);
