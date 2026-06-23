using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Features.Categories.Queries;

namespace Souq.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    public CategoriesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _mediator.Send(new GetCategoriesQuery()));
}
