using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Features.Reviews.Commands;
using Souq.Application.Features.Reviews.Queries;

namespace Souq.API.Controllers;

[ApiController]
[Route("api/products/{productId:int}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ReviewsController(IMediator mediator) => _mediator = mediator;

    // GET /api/products/5/reviews — عام: قائمة التقييمات + المتوسط.
    [HttpGet]
    public async Task<IActionResult> GetAll(int productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(await _mediator.Send(new GetProductReviewsQuery(productId, page, pageSize)));

    // POST /api/products/5/reviews — يتطلّب تسجيل الدخول. هويّة العميل من التوكن،
    // لا من الجسم (نفس مبدأ الطلبات: لا انتحال تقييمات باسم عميل آخر).
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(int productId, [FromBody] CreateReviewRequest body)
    {
        var result = await _mediator.Send(new CreateReviewCommand(productId, CurrentUserId(), body.Rating, body.Comment));
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        return StatusCode(StatusCodes.Status201Created, new { id = result.Value });
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public record CreateReviewRequest(int Rating, string Comment);
