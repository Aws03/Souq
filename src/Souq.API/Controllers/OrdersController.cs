using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Features.Orders.Queries;

namespace Souq.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrdersController(IMediator mediator) => _mediator = mediator;

    // POST /api/orders — إتمام الطلب والدفع.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.IsSuccess)
            // 400 مع رمز الخطأ التجاري (InsufficientStock / PaymentFailed...).
            return BadRequest(new { error = result.Error, code = result.ErrorCode });

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.OrderId }, result.Value);
    }

    // GET /api/orders/5 — متابعة الطلب.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id));
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.Error });
    }
}
