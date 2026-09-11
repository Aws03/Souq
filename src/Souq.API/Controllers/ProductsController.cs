using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Enums;

namespace Souq.API.Controllers;

// ============================================================================
// ProductsController — "Thin Controller". لا منطق أعمال هنا إطلاقاً. مهمته الوحيدة:
// ترجمة طلب HTTP → رسالة MediatR، ثم ترجمة النتيجة → استجابة HTTP. قرار الصلاحية معلن
// صراحةً على كل نقطة: عامة ([AllowAnonymous]) أو صلاحية (HasPermission) — اختبار يرفض غير ذلك.
// ============================================================================
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProductsController(IMediator mediator) => _mediator = mediator;

    // GET /api/products?keyword=&categoryIds=1&categoryIds=2&minPrice=&maxPrice=&sortBy=Newest&page=1&pageSize=12
    // categoryIds تتكرّر كمفتاح لكل فئة (الربط القياسي لـ List<int>). المدخلات تُتحقَّق
    // في GetProductsQueryValidator (صفحة ≥ 1، حجم 1–100) ⇒ 400 بدل خطأ SQL.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? keyword, [FromQuery] List<int>? categoryIds,
        [FromQuery] decimal? minPrice, [FromQuery] decimal? maxPrice,
        [FromQuery] ProductSortBy sortBy = ProductSortBy.Newest,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
        => Ok(await _mediator.Send(
            new GetProductsQuery(keyword, categoryIds, page, pageSize, minPrice, maxPrice, sortBy)));

    // GET /api/products/5
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _mediator.Send(new GetProductByIdQuery(id));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // GET /api/products/5/related — منتجات ذات صلة.
    [HttpGet("{id:int}/related")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRelated(int id, [FromQuery] int count = 6)
    {
        var result = await _mediator.Send(new GetRelatedProductsQuery(id, count));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // POST /api/products — ينشئ منتجاً.
    [HttpPost]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.IsSuccess) return this.Failure(result);
        // 201 Created مع رابط المورد الجديد (ممارسة REST صحيحة).
        return CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value });
    }

    // PUT /api/products/5 — معرّف المسار هو مصدر الحقيقة لا جسم الطلب. تعديل مخزون من
    // نموذج قديم ⇒ 409 StockChanged (compare-and-set عبر expectedStockQuantity).
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // DELETE /api/products/5 — حذف منطقي (تعطيل) للمنتج.
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Catalog.Manage)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteProductCommand(id));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // POST /api/products/5/image — multipart/form-data. هنا اهتمامات HTTP فقط (وجود الملف +
    // سقف حجم الطلب)؛ نوع الملف الحقيقي يُكشف من محتواه في حالة الاستخدام — Content-Type
    // واسم الملف القادمان من العميل لا يُستخدمان (ADR-0016).
    [HttpPost("{id:int}/image")]
    [HasPermission(Permissions.Catalog.Manage)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.Failure(Error.Validation("FileRequired", "لم يُرفق ملف صورة"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadProductImageCommand(id, stream, file.Length));
        return result.IsSuccess ? Ok(new { imageUrl = result.Value }) : this.Failure(result);
    }

    // POST /api/products/5/video — نفس منهج UploadImage تماماً.
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
