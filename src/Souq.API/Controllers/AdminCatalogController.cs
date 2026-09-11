using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Categories.Queries;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Enums;

namespace Souq.API.Controllers;

// ============================================================================
// إدارة الكتالوج (المرحلة 5، catalog.manage): كل الحالات (المسودّة والمؤرشف يظهران هنا ليُنشرا أو يُستعادا —
// C7)، نموذج التعديل الكامل بكل اللغات والصور، دورة الحياة، ترتيب الصور وحذفها، وكل الفئات بما فيها المعطّلة.
// كل معرّف هنا من متجر المضيف وحده (المستودعات مُرشَّحة) — معرّف متجر آخر ⇒ 404.
// ============================================================================
[ApiController]
[Route("api/admin")]
[HasPermission(Permissions.Catalog.Manage)]
public class AdminCatalogController : ControllerBase
{
    private readonly IMediator _mediator;
    public AdminCatalogController(IMediator mediator) => _mediator = mediator;

    // GET /api/admin/products?keyword=&status=Draft&categoryId=&sortBy=Newest&page=1&pageSize=20
    [HttpGet("products")]
    public async Task<IActionResult> ListProducts(
        [FromQuery] string? keyword, [FromQuery] ProductStatus? status, [FromQuery] int? categoryId,
        [FromQuery] AdminProductSortBy sortBy = AdminProductSortBy.Newest, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListAdminProductsQuery(keyword, status, categoryId, sortBy, page, pageSize)));

    [HttpGet("products/{id:int}")]
    public async Task<IActionResult> GetProduct(int id) => this.ToHttp(await _mediator.Send(new GetAdminProductQuery(id)));

    // { "status": "Active" | "Draft" | "Archived" } — نشر، إخفاء مؤقّت، أرشفة، أو استعادة.
    [HttpPut("products/{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] ProductStatusRequest body)
        => this.ToHttp(await _mediator.Send(new ChangeProductStatusCommand(id, body.Status)));

    [HttpDelete("products/{id:int}/images/{imageId:int}")]
    public async Task<IActionResult> RemoveImage(int id, int imageId)
        => this.ToHttp(await _mediator.Send(new RemoveProductImageCommand(id, imageId)));

    // { "imageIds": [7, 3, 9] } — كل صور المنتج بترتيبها الجديد (الأولى رئيسية).
    [HttpPut("products/{id:int}/images/order")]
    public async Task<IActionResult> ReorderImages(int id, [FromBody] ImageOrderRequest body)
        => this.ToHttp(await _mediator.Send(new ReorderProductImagesCommand(id, body.ImageIds ?? [])));

    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories() => Ok(await _mediator.Send(new ListAdminCategoriesQuery()));
}

public record ProductStatusRequest(ProductStatus Status);
public record ImageOrderRequest(IReadOnlyList<int>? ImageIds);
