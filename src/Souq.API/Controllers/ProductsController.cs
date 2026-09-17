using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;

namespace Souq.API.Controllers;

// ============================================================================
// ProductsController — "Thin Controller". لا منطق أعمال هنا إطلاقاً: ترجمة طلب HTTP → رسالة MediatR، ثم النتيجة →
// استجابة HTTP. قرار الصلاحية معلن صراحةً على كل نقطة: عامة ([AllowAnonymous]) أو صلاحية (HasPermission).
// القراءة العامة: المعروض فقط. الإدارة الكاملة (كل الحالات، الترتيب، الصور) في AdminCatalogController.
// ============================================================================
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProductsController(IMediator mediator) => _mediator = mediator;

    // GET /api/products?keyword=&categoryIds=1&categoryIds=2&minPrice=&maxPrice=&sortBy=Newest&onSale=false&page=1&pageSize=12
    // المدخلات تُتحقَّق في GetProductsQueryValidator (صفحة ≥ 1، حجم 1–100) ⇒ 400 بدل خطأ SQL.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? keyword, [FromQuery] List<int>? categoryIds,
        [FromQuery] decimal? minPrice, [FromQuery] decimal? maxPrice,
        [FromQuery] ProductSortBy sortBy = ProductSortBy.Newest, [FromQuery] bool onSale = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12, [FromQuery] bool exact = false)
        => Ok(await _mediator.Send(
            new GetProductsQuery(keyword, categoryIds, page, pageSize, minPrice, maxPrice, sortBy, onSale, exact)));

    // GET /api/products/suggestions?q=مكن&limit=8 — اقتراحات أثناء الكتابة (M3، ADR-0042).
    // منتجات معروضة ثم فئات مفعَّلة، بالترتيب نفسه الذي ترتّب به صفحة النتائج — لا تصحيح خطأ مطبعي هنا:
    // المتسوّق ما زال يكتب. المسار قبل "{id:int}" كي لا تُفسَّر "suggestions" كمعرّف.
    [HttpGet("suggestions")]
    [AllowAnonymous]
    public async Task<IActionResult> Suggestions(
        [FromQuery] string? q, [FromQuery] int limit = SearchSuggestionRules.DefaultLimit)
        => Ok(await _mediator.Send(new GetSearchSuggestionsQuery(q, limit)));

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id) => this.ToHttp(await _mediator.Send(new GetProductByIdQuery(id)));

    // GET /api/products/by-slug/wireless-headphones — روابط مقروءة ومحرّكات البحث.
    [HttpGet("by-slug/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBySlug(string slug) => this.ToHttp(await _mediator.Send(new GetProductBySlugQuery(slug)));

    [HttpGet("{id:int}/related")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRelated(int id, [FromQuery] int count = 6)
        => this.ToHttp(await _mediator.Send(new GetRelatedProductsQuery(id, count)));

    [HttpPost]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.IsSuccess) return this.Failure(result);
        return CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value });
    }

    // PUT /api/products/5 — معرّف المسار هو مصدر الحقيقة لا جسم الطلب. لا مخزون هنا (المرحلة 6): تصحيحاته في
    // POST /api/admin/inventory/{id}/adjustments.
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductCommand command)
        => this.ToHttp(await _mediator.Send(command with { Id = id }));

    // DELETE /api/products/5 — أرشفة (لا حذف أبداً)؛ الاستعادة بتغيير الحالة.
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Delete(int id) => this.ToHttp(await _mediator.Send(new DeleteProductCommand(id)));

    // POST /api/products/5/image — multipart. صورة تُضاف لمعرض المنتج (حتى 10). اهتمامات HTTP فقط هنا؛ نوع
    // الملف الحقيقي يُكشف من محتواه في حالة الاستخدام (ADR-0016).
    [HttpPost("{id:int}/image")]
    [HasPermission(Permissions.Catalog.Manage)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.Failure(Error.Validation("FileRequired", "لم يُرفق ملف صورة"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadProductImageCommand(id, stream, file.Length));
        return result.IsSuccess
            ? Ok(new { id = result.Value!.Id, imageUrl = result.Value.Url, sortOrder = result.Value.SortOrder })
            : this.Failure(result);
    }

    [HttpPost("{id:int}/video")]
    [HasPermission(Permissions.Catalog.Manage)]
    [RequestSizeLimit(55 * 1024 * 1024)]
    public async Task<IActionResult> UploadVideo(int id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.Failure(Error.Validation("FileRequired", "لم يُرفق ملف فيديو"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadProductVideoCommand(id, stream, file.Length));
        return result.IsSuccess ? Ok(new { videoUrl = result.Value }) : this.Failure(result);
    }
}
