using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Categories.Commands;
using Souq.Application.Features.Categories.Queries;

namespace Souq.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    public CategoriesController(IMediator mediator) => _mediator = mediator;

    // GET /api/categories — عام (يحتاجه المتجر لعرض الفئات).
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
        => Ok(await _mediator.Send(new GetCategoriesQuery()));

    // POST /api/categories — ينشئ فئة.
    [HttpPost]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, new { id = result.Value }) : this.Failure(result);
    }

    // PUT /api/categories/5 — يفرض معرّف المسار على الأمر.
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // DELETE /api/categories/5 — حذف محروس (لا فئة مستخدمة/لها أبناء ⇒ 409).
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteCategoryCommand(id));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }
}
