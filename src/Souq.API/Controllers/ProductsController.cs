using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Common.Models;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Common;

namespace Souq.API.Controllers;

// ============================================================================
// ProductsController — "Thin Controller". لاحظ كم هو نحيف!
// لا منطق أعمال هنا إطلاقاً. مهمته الوحيدة: ترجمة طلب HTTP → رسالة MediatR،
// ثم ترجمة النتيجة → استجابة HTTP. كل القرارات الفعلية في طبقة Application.
// هذا يعني أن استبدال REST بـ gRPC مثلاً لن يمسّ منطق الأعمال أبداً.
// ============================================================================
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProductsController(IMediator mediator) => _mediator = mediator;

    // GET /api/products?keyword=&categoryId=&page=1&pageSize=12
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? keyword, [FromQuery] int? categoryId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var result = await _mediator.Send(new GetProductsQuery(keyword, categoryId, page, pageSize));
        return Ok(result);
    }

    // GET /api/products/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _mediator.Send(new GetProductByIdQuery(id));
        // نترجم Result الداخلي إلى رمز HTTP مناسب.
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    // POST /api/products  (للمدير) — ينشئ منتجاً. محمي: دور Admin فقط.
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error, code = result.ErrorCode });
        // 201 Created مع رابط المورد الجديد (ممارسة REST صحيحة).
        return CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value });
    }

    // PUT /api/products/5  (للمدير) — يحدّث منتجاً قائماً بالكامل.
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductCommand command)
    {
        // نفرض معرّف المسار على الأمر؛ معرّف المسار هو مصدر الحقيقة لا جسم الطلب
        // (يمنع تحديث منتج عبر معرّف مختلف مدسوس في الجسم).
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : MapFailure(result);
    }

    // DELETE /api/products/5  (للمدير) — حذف منطقي (تعطيل) للمنتج.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteProductCommand(id));
        return result.IsSuccess ? NoContent() : MapFailure(result);
    }

    // POST /api/products/5/image  (للمدير) — يرفع صورة المنتج (multipart/form-data).
    // التحقّق الشكلي للملف (وجوده، حجمه، نوعه) اهتمام HTTP فنحسمه هنا قبل الأمر؛
    // التخزين نفسه خلف IFileStorage في طبقة Application.
    [HttpPost("{id:int}/image")]
    [Authorize(Roles = Roles.Admin)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "لم يُرفق ملف صورة" });
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "حجم الصورة يتجاوز 5 ميغابايت" });

        var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowed.Contains(file.ContentType))
            return BadRequest(new { error = "صيغة الصورة غير مدعومة (JPEG/PNG/WebP/GIF فقط)" });

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadProductImageCommand(id, stream, file.FileName));
        if (!result.IsSuccess)
            return result.ErrorCode == "NotFound"
                ? NotFound(new { error = result.Error })
                : BadRequest(new { error = result.Error, code = result.ErrorCode });

        return Ok(new { imageUrl = result.Value });
    }

    // ترجمة فشل Result إلى رمز HTTP موحّد: "غير موجود" ⇒ 404، وأي فشل عمل آخر ⇒ 400.
    // مكان واحد يحكم هذا التحويل لكل الأوامر التي لا تُرجع قيمة (DRY).
    private IActionResult MapFailure(Result result) =>
        result.ErrorCode == "NotFound"
            ? NotFound(new { error = result.Error })
            : BadRequest(new { error = result.Error, code = result.ErrorCode });
}
