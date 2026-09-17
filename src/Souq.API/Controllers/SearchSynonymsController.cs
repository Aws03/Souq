using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;

namespace Souq.API.Controllers;

// ============================================================================
// مفردات بحث المتجر (M3، ADR-0042) — شاشة التاجر.
//
// الصلاحية هي صلاحية الكتالوج نفسها (`catalog.manage`) لا صلاحية جديدة: المفردات إعدادُ كتالوجٍ يغيّر ما
// يجده الزبائن، ومن يملك تعديل المنتجات والفئات يملك هذا. صلاحية منفصلة كانت ستُضيف بُعداً في جدول الأدوار
// بلا فرق حقيقي في من يستعملها.
// ============================================================================
[ApiController]
[Route("api/admin/search-synonyms")]
[HasPermission(Permissions.Catalog.Manage)]
public class SearchSynonymsController : ControllerBase
{
    private readonly IMediator _mediator;
    public SearchSynonymsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _mediator.Send(new ListSearchSynonymsQuery()));

    // { "culture": "ar", "term": "جوال", "expansion": "هاتف" }
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSearchSynonymCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new { id = result.Value })
            : this.Failure(result);
    }

    // المعرّف من المسار هو المرجع، لا الذي في الجسم — وإلا عدّل صفّاً غير الذي في الرابط.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSearchSynonymCommand command) =>
        this.ToHttp(await _mediator.Send(command with { Id = id }));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) => this.ToHttp(await _mediator.Send(new DeleteSearchSynonymCommand(id)));
}
