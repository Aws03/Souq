using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Reporting;

namespace Souq.API.Controllers;

// ============================================================================
// تقارير المتجر لطاقمه (store.reports.view — كانت الصلاحية معرّفة بلا نقطة تستعملها).
//
// نقطة واحدة تُرجع اللوحة كاملة، لا نقطة لكل بطاقة: اثنتا عشرة بطاقة تعني اثني عشر طلباً
// ورحلة ذهاب وإياب لكل منها، وكلّها تقرأ الجداول نفسها في المدّة نفسها. استجابة واحدة تجعل
// اللوحة متّسقة أيضاً — بطاقاتها من لحظة واحدة لا من اثنتي عشرة لحظة متفرّقة.
//
// المدّة مفتاح من قائمة مغلقة (ReportRange) لا تاريخان من المتصفّح: مدى حرّ يعني مسحاً
// غير محدود يختاره المتصل، وهو مسار إساءة استعمال رخيص. النطاق يأتي من المضيف لا من الطلب.
// ============================================================================
[ApiController]
[Route("api/admin/reports")]
[HasPermission(Permissions.Store.Reports)]
public class StoreReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    public StoreReportsController(IMediator mediator) => _mediator = mediator;

    // GET /api/admin/reports/dashboard?range=Last30Days
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] ReportRange range = ReportRange.Last30Days)
        => Ok(await _mediator.Send(new GetStoreDashboardQuery(range)));
}
