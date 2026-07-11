using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Common.Models;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Queries;

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

    // POST /api/products  (للمدير) — ينشئ منتجاً.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        // 201 Created مع رابط المورد الجديد (ممارسة REST صحيحة).
        return CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value });
    }

    // PUT /api/products/5  (للمدير) — يحدّث منتجاً قائماً بالكامل.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductCommand command)
    {
        // نفرض معرّف المسار على الأمر؛ معرّف المسار هو مصدر الحقيقة لا جسم الطلب
        // (يمنع تحديث منتج عبر معرّف مختلف مدسوس في الجسم).
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : MapFailure(result);
    }

    // DELETE /api/products/5  (للمدير) — حذف منطقي (تعطيل) للمنتج.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteProductCommand(id));
        return result.IsSuccess ? NoContent() : MapFailure(result);
    }

    // ترجمة فشل Result إلى رمز HTTP موحّد: "غير موجود" ⇒ 404، وأي فشل عمل آخر ⇒ 400.
    // مكان واحد يحكم هذا التحويل لكل الأوامر التي لا تُرجع قيمة (DRY).
    private IActionResult MapFailure(Result result) =>
        result.ErrorCode == "NotFound"
            ? NotFound(new { error = result.Error })
            : BadRequest(new { error = result.Error, code = result.ErrorCode });
}
