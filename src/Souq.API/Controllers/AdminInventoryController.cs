using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Features.Inventory.Queries;
using Souq.Domain.Common;

namespace Souq.API.Controllers;

// ============================================================================
// AdminInventoryController — نقاط جرد المخزون، كلها للمدير فقط. مسار مستقلّ
// (api/admin/inventory) يفصل شؤون الإدارة عن نقاط المتجر العامة. Thin Controller:
// يترجم HTTP → رسالة MediatR فقط، لا منطق أعمال هنا.
// ============================================================================
[ApiController]
[Route("api/admin/inventory")]
[Authorize(Roles = Roles.Admin)]
public class AdminInventoryController : ControllerBase
{
    private readonly IMediator _mediator;
    public AdminInventoryController(IMediator mediator) => _mediator = mediator;

    // GET /api/admin/inventory — جرد كامل: كل المنتجات النشطة بمخزونها الحالي.
    [HttpGet]
    public async Task<IActionResult> GetInventory()
        => Ok(await _mediator.Send(new GetInventoryQuery()));

    // GET /api/admin/inventory/low-stock — المنتجات المنخفضة المخزون فقط (للتنبيه).
    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStock()
        => Ok(await _mediator.Send(new GetLowStockQuery()));

    // GET /api/admin/inventory/5/movements — سجلّ حركة مخزون منتج (الأحدث أولاً).
    [HttpGet("{productId:int}/movements")]
    public async Task<IActionResult> GetMovements(int productId)
        => Ok(await _mediator.Send(new GetStockMovementsQuery(productId)));
}
