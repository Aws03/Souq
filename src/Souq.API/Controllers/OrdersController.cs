using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Security;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Features.Orders.Queries;
using Souq.Domain.Enums;

namespace Souq.API.Controllers;

// كل نقاط الطلبات تتطلّب تسجيل الدخول إلا رابط التتبّع العام بالرمز. الهوية وفحص الملكية يعيشان في Application
// (ICurrentUser) — هذا الـ Controller لا يقرأ المطالبات ولا يقرّر من يرى ماذا (Phase 0 B7).
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrdersController(IMediator mediator) => _mediator = mediator;

    // POST /api/orders — إتمام الطلب والدفع. العميل هو المستخدم الحالي دائماً (من التوكن، لا من الجسم). بلا items ⇒ من
    // سلة العميل (المرحلة 9).
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

    // POST /api/orders/5/cancel { "reason": "…" } — العميل صاحب الطلب، قبل الدفع فقط (المرحلة 9). غيره 404.
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(
        int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelOrderRequest? body)
    {
        var result = await _mediator.Send(new CancelMyOrderCommand(id, body?.Reason));
        return result.IsSuccess ? NoContent() : this.Failure(result);
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

    // GET /api/orders/track/{token} — رابط تتبّع قابل للمشاركة بلا مصادقة عمداً، بالرمز العشوائي لا بالمعرّف (المرحلة 9،
    // B8). العقد يكشف الحدّ الأدنى فقط.
    [HttpGet("track/{token}")]
    [AllowAnonymous]
    // متاحة والمتجر موقوف (C-17 = B): المشتري دفع ثمن طلبه قبل الإيقاف، والإيقاف مسألة بين المنصّة
    // والتاجر لا ذنب له فيها — فحجبُ تتبّع طلبه عنه عقوبةٌ على غير المخطئ. ولا تُفتح للمؤرشف:
    // الأرشفة نهائية.
    [AvailableWhenStoreSuspended]
    public async Task<IActionResult> Track(string token)
    {
        var result = await _mediator.Send(new GetOrderTrackingQuery(token));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/orders?status=Paid&search=1042&from=&to=&customerId= — طلبات المتجر مرقّمة لشاشة الإدارة (المرحلة 9: مرشّحات).
    [HttpGet]
    [HasPermission(Permissions.Orders.View)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? customerId = null,
        [FromQuery] OrderStatus? status = null, [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        => Ok(await _mediator.Send(new GetOrdersQuery(page, pageSize, customerId, status, search, from, to)));

    // PUT /api/orders/5/status — شحن/تسليم/إلغاء. الإلغاء يعيد المخزون المحجوز.
    [HttpPut("{id:int}/status")]
    [HasPermission(Permissions.Orders.Manage)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest body)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand(
            id, body.Action!.Value, body.Note, body.TrackingNumber, body.ShippingCarrier));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }
}

// جسم طلب تحديث الحالة. الإجراء enum يُرسَل كنص ("Ship"/"Deliver"/"Cancel").
//
// الحقل قابل للعدم و[Required] عمداً: بنوع غير قابل للعدم كان الجسم الفارغ {} يُقبل — الربط يملأ العضو صفراً، وهو
// هنا Ship، فيُشحن الطلب بجواب 204 دون أن يطلب أحد ذلك. IsInEnum لا يكشفه لأن صفراً عضو معرّف. القابلية للعدم
// تفصل "غائب" عن "العضو صفر"، والتحقّق التلقائي في [ApiController] يردّ 400 ValidationFailed بالعقد نفسه.
// [Required] بلا محدِّد هدف: في السجلّ الموضعي يقع الوسم على معامل المُنشئ، وهو ما يقرؤه التحقّق. بـ [property:]
// يرمي MVC صراحةً (ThrowIfRecordTypeHasValidationOnProperties) لأن الوسم على الخاصية يُتجاهَل.
public record UpdateOrderStatusRequest(
    [Required] OrderStatusAction? Action,
    string? Note = null, string? TrackingNumber = null, string? ShippingCarrier = null);

public record CancelOrderRequest(string? Reason = null);
