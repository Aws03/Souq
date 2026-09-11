using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Security;
using Souq.Application.Features.Reviews.Commands;
using Souq.Application.Features.Reviews.Moderation;
using Souq.Application.Features.Reviews.Queries;
using Souq.Application.Features.Stores;
using Souq.Domain.Enums;
using Souq.Domain.Platform;

namespace Souq.API.Controllers;

// وحدة Reviews اختيارية (D-11): معطّلة للمتجر ⇒ القائمة والإنشاء والإشراف كلها 404 ModuleDisabled.
[ApiController]
[Route("api/products/{productId:int}/reviews")]
[RequiresModule(StoreModules.Reviews)]
public class ReviewsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ReviewsController(IMediator mediator) => _mediator = mediator;

    // GET /api/products/5/reviews — عام: التقييمات المعتمدة + المتوسط والتوزيع.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(int productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(await _mediator.Send(new GetProductReviewsQuery(productId, page, pageSize)));

    // POST /api/products/5/reviews — يتطلّب تسجيل الدخول. المقيِّم هو المستخدم الحالي في
    // حالة الاستخدام (ICurrentUser) — لا انتحال تقييمات باسم عميل آخر. الرد { id, status }.
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(int productId, [FromBody] CreateReviewRequest body)
    {
        var result = await _mediator.Send(new CreateReviewCommand(productId, body.Rating, body.Comment));
        if (!result.IsSuccess)
            return this.Failure(result);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }
}

public record CreateReviewRequest(int Rating, string Comment);

// ============================================================================
// الإشراف على التقييمات (المرحلة 13، reviews.moderate): الطابور بكل الحالات، والاعتماد والرفض. سياسة النشر (اعتماد تلقائي)
// إعداد متجر: يقرأها المشرف، ويغيّرها من يملك store.settings.manage أيضاً (الصلاحيتان معاً على PUT).
// ============================================================================
[ApiController]
[Route("api/admin/reviews")]
[RequiresModule(StoreModules.Reviews)]
[HasPermission(Permissions.Reviews.Moderate)]
public class ReviewModerationController : ControllerBase
{
    private readonly IMediator _mediator;
    public ReviewModerationController(IMediator mediator) => _mediator = mediator;

    // GET /api/admin/reviews?status=Pending&productId=5&page=1 — شارة "بانتظار المراجعة" من status=Pending&pageSize=1.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ReviewStatus? status, [FromQuery] int? productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListReviewsForModerationQuery(status, productId, page, pageSize)));

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id) => this.ToHttp(await _mediator.Send(new ApproveReviewCommand(id)));

    // { "note": "..." } — ملاحظة للإدارة وحدها، لا تُعرض للعميل.
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectReviewRequest body)
        => this.ToHttp(await _mediator.Send(new RejectReviewCommand(id, body.Note)));

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(await _mediator.Send(new GetReviewSettingsQuery()));

    [HttpPut("settings")]
    [HasPermission(Permissions.Store.Settings)]
    public async Task<IActionResult> UpdateSettings([FromBody] ReviewSettingsDto body)
        => this.ToHttp(await _mediator.Send(new UpdateReviewSettingsCommand(body.AutoApprove)));
}

public record RejectReviewRequest(string? Note);
