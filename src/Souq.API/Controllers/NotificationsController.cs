using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.Application.Features.Notifications;

namespace Souq.API.Controllers;

// ============================================================================
// إشعارات الحساب الحالي داخل التطبيق (المرحلة 14): للعميل عن طلباته، وللإدارة عن طلب جديد أو مخزون ينفد. لا معرّف حساب في
// أي مسار — المستلم هو المتصل؛ إشعار غيره ⇒ 404. الشارة تسأل unread-count دورياً، والقائمة تُجلب عند فتحها.
// ============================================================================
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    public NotificationsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(await _mediator.Send(new ListMyNotificationsQuery(unreadOnly, page, pageSize)));

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() => Ok(new { count = await _mediator.Send(new CountMyUnreadNotificationsQuery()) });

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id) => this.ToHttp(await _mediator.Send(new MarkNotificationReadCommand(id)));

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead() => this.ToHttp(await _mediator.Send(new MarkAllNotificationsReadCommand()));
}
