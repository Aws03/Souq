using System.ComponentModel.DataAnnotations;
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
        => this.ToHttp(await _mediator.Send(new ChangeProductStatusCommand(id, body.Status!.Value)));

    [HttpDelete("products/{id:int}/images/{imageId:int}")]
    public async Task<IActionResult> RemoveImage(int id, int imageId)
        => this.ToHttp(await _mediator.Send(new RemoveProductImageCommand(id, imageId)));

    // { "imageIds": [7, 3, 9] } — كل صور المنتج بترتيبها الجديد (الأولى رئيسية).
    [HttpPut("products/{id:int}/images/order")]
    public async Task<IActionResult> ReorderImages(int id, [FromBody] ImageOrderRequest body)
        => this.ToHttp(await _mediator.Send(new ReorderProductImagesCommand(id, body.ImageIds ?? [])));

    // ── الخيارات والمتغيّرات (ADR-0040). المتغيّر يُحلّ داخل المنتج في المسار: متغيّر منتج آخر ⇒ 404 ──

    // PUT /api/admin/products/5/options { "options": [{ "id": 3, "names": { "ar": "المقاس" }, "values": [...] }] }
    [HttpPut("products/{id:int}/options")]
    public async Task<IActionResult> SetOptions(int id, [FromBody] ProductOptionsRequest body)
        => this.ToHttp(await _mediator.Send(new SetProductOptionsCommand(id, body.Options ?? [])));

    // POST /api/admin/products/5/variants { "variants": [{ "optionValueIds": [7, 12], "price": 25, "initialStock": 4 }] }
    [HttpPost("products/{id:int}/variants")]
    public async Task<IActionResult> CreateVariants(int id, [FromBody] ProductVariantsRequest body)
    {
        var result = await _mediator.Send(new CreateProductVariantsCommand(id, body.Variants ?? []));
        return result.IsSuccess ? Ok(new { ids = result.Value }) : this.Failure(result);
    }

    // PUT /api/admin/products/5/variants/12 { "price": 25, "compareAtPrice": null, "sku": "SHIRT-L" }
    [HttpPut("products/{id:int}/variants/{variantId:int}")]
    public async Task<IActionResult> UpdateVariant(int id, int variantId, [FromBody] ProductVariantPricingRequest body)
        => this.ToHttp(await _mediator.Send(new UpdateProductVariantCommand(id, variantId, body.Price!.Value, body.CompareAtPrice, body.Sku, body.Cost)));

    // PUT /api/admin/products/5/variants/12/status { "isActive": false } — تعطيل بدل حذف.
    [HttpPut("products/{id:int}/variants/{variantId:int}/status")]
    public async Task<IActionResult> SetVariantStatus(int id, int variantId, [FromBody] ProductVariantStatusRequest body)
        => this.ToHttp(await _mediator.Send(new SetProductVariantStatusCommand(id, variantId, body.IsActive!.Value)));

    // PUT /api/admin/products/5/variants/12/default
    [HttpPut("products/{id:int}/variants/{variantId:int}/default")]
    public async Task<IActionResult> SetDefaultVariant(int id, int variantId)
        => this.ToHttp(await _mediator.Send(new SetDefaultProductVariantCommand(id, variantId)));

    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories() => Ok(await _mediator.Send(new ListAdminCategoriesQuery()));
}

// قابل للعدم و[Required]: بنوع غير قابل للعدم كان {} يعني Draft (العضو صفر) فيُخفى المنتج من المتجر بجواب ناجح.
public record ProductStatusRequest([Required] ProductStatus? Status);
public record ImageOrderRequest(IReadOnlyList<int>? ImageIds);
public record ProductOptionsRequest(IReadOnlyList<ProductOptionInput>? Options);
public record ProductVariantsRequest(IReadOnlyList<NewProductVariantInput>? Variants);

// قابلة للعدم و[Required] للسبب نفسه: {} لا يعني سعر صفر ولا تعطيلاً صامتاً.
// Cost: تكلفة الوحدة — سرٌّ تجاري لا يخرج إلا في استجابات الإدارة (C11). `null` تمسحها.
public record ProductVariantPricingRequest([Required] decimal? Price, decimal? CompareAtPrice, string? Sku, decimal? Cost);
public record ProductVariantStatusRequest([Required] bool? IsActive);
