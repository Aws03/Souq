using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Inventory.Commands;
using Souq.Application.Features.Inventory.Queries;

namespace Souq.API.Controllers;

// ============================================================================
// AdminInventoryController — نقاط المخزون، القراءة بصلاحية inventory.view والتعديل بـ inventory.manage أيضاً. مسار
// مستقلّ (api/admin/inventory) يفصل شؤون الإدارة عن نقاط المتجر العامة. كل القوائم مرقّمة (حدّ أقصى 100).
// المرحلة 6: الموجود والمحجوز والمتاح لكل منتج، والتعديل تصحيحات بفارق وسبب لا تعيين مطلق (Phase 0 C4).
// ============================================================================
[ApiController]
[Route("api/admin/inventory")]
[HasPermission(Permissions.Inventory.View)]
public class AdminInventoryController : ControllerBase
{
    private readonly IMediator _mediator;
    public AdminInventoryController(IMediator mediator) => _mediator = mediator;

    // GET /api/admin/inventory — مخزون المنتجات غير المؤرشفة، الأقلّ متاحاً أولاً.
    [HttpGet]
    public async Task<IActionResult> GetInventory([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _mediator.Send(new GetInventoryQuery(page, pageSize)));

    // GET /api/admin/inventory/low-stock — المنخفضة فقط (totalCount يكفي للشارة).
    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStock([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new GetLowStockQuery(page, pageSize)));

    // GET /api/admin/inventory/5/movements — سجلّ حركة مخزون منتج (الأحدث أولاً).
    [HttpGet("{productId:int}/movements")]
    public async Task<IActionResult> GetMovements(int productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _mediator.Send(new GetStockMovementsQuery(productId, page, pageSize)));

    // POST /api/admin/inventory/5/adjustments { "delta": -2, "reason": "تلف أثناء النقل" } — يعيد مستوى المخزون الجديد.
    [HttpPost("{productId:int}/adjustments")]
    [HasPermission(Permissions.Inventory.Manage)]
    public async Task<IActionResult> Adjust(int productId, [FromBody] StockAdjustmentRequest body)
        => this.ToHttp(await _mediator.Send(new AdjustStockCommand(productId, body.Delta, body.Reason ?? "")));

    // PUT /api/admin/inventory/5/threshold { "lowStockThreshold": 3 }
    [HttpPut("{productId:int}/threshold")]
    [HasPermission(Permissions.Inventory.Manage)]
    public async Task<IActionResult> SetThreshold(int productId, [FromBody] StockThresholdRequest body)
        => this.ToHttp(await _mediator.Send(new SetLowStockThresholdCommand(productId, body.LowStockThreshold)));
}

public record StockAdjustmentRequest(int Delta, string? Reason);
public record StockThresholdRequest(int LowStockThreshold);
