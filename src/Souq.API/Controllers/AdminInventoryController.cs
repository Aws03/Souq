using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Inventory.Queries;

namespace Souq.API.Controllers;

// ============================================================================
// AdminInventoryController — نقاط جرد المخزون، كلها بصلاحية inventory.view. مسار مستقلّ
// (api/admin/inventory) يفصل شؤون الإدارة عن نقاط المتجر العامة. كل القوائم مرقّمة
// (page/pageSize، حدّ أقصى 100) — كانت تعيد كل المنتجات وكل سجلّ الحركة دفعة واحدة.
// ============================================================================
[ApiController]
[Route("api/admin/inventory")]
[HasPermission(Permissions.Inventory.View)]
public class AdminInventoryController : ControllerBase
{
    private readonly IMediator _mediator;
    public AdminInventoryController(IMediator mediator) => _mediator = mediator;

    // GET /api/admin/inventory — المنتجات النشطة بمخزونها، الأقلّ مخزوناً أولاً.
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
}
