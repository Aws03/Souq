using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Tenancy;
using Souq.Domain.Platform;
using Souq.Application.Features.Reviews.Commands;
using Souq.Application.Features.Reviews.Queries;

namespace Souq.API.Controllers;

// وحدة Reviews اختيارية (D-11): معطّلة للمتجر ⇒ القائمة والإنشاء 404 ModuleDisabled.
[ApiController]
[Route("api/products/{productId:int}/reviews")]
[RequiresModule(StoreModules.Reviews)]
public class ReviewsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ReviewsController(IMediator mediator) => _mediator = mediator;

    // GET /api/products/5/reviews — عام: قائمة التقييمات + المتوسط.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(int productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(await _mediator.Send(new GetProductReviewsQuery(productId, page, pageSize)));

    // POST /api/products/5/reviews — يتطلّب تسجيل الدخول. المقيِّم هو المستخدم الحالي في
    // حالة الاستخدام (ICurrentUser) — لا انتحال تقييمات باسم عميل آخر.
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(int productId, [FromBody] CreateReviewRequest body)
    {
        var result = await _mediator.Send(new CreateReviewCommand(productId, body.Rating, body.Comment));
        if (!result.IsSuccess)
            return this.Failure(result);
        return StatusCode(StatusCodes.Status201Created, new { id = result.Value });
    }
}

public record CreateReviewRequest(int Rating, string Comment);
