using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.Application.Features.Categories.Commands;
using Souq.Application.Features.Categories.Queries;
using Souq.Domain.Common;

namespace Souq.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    public CategoriesController(IMediator mediator) => _mediator = mediator;

    // GET /api/categories — عام (يحتاجه المتجر لعرض الفئات).
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _mediator.Send(new GetCategoriesQuery()));

    // POST /api/categories  (مدير) — ينشئ فئة.
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, new { id = result.Value }) : this.Failure(result);
    }

    // PUT /api/categories/5  (مدير) — يفرض معرّف المسار على الأمر.
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // DELETE /api/categories/5  (مدير) — حذف محروس (لا فئة مستخدمة/لها أبناء).
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteCategoryCommand(id));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }
}
