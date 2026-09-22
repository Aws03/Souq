using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Subscriptions;

namespace Souq.API.Controllers;

// ============================================================================
// اشتراكُ التاجر كما يراه هو (C5، [ADR-0056](0056)): خطتُه، وما عليه، وفواتيرُه، وكيف يدفع.
//
// **على مضيف متجره، وبلا معرّف متجرٍ في أيّ مسار أو جسم.** المتجرُ من المضيف كأيّ نقطة متجر —
// فـ«فاتورةُ تاجرٍ آخر» غيرُ قابلة للطلب أصلاً، لا مرفوضةٌ بفحص.
//
// **والصلاحيةُ `store.settings.manage` لا صلاحيةٌ جديدة.** ولم تُخترَع سادسةٌ عمداً: الاشتراكُ
// والمستحقُّ شأنُ **صاحب المتجر** لا عملَ موظّفه اليوميّ، وهذه الصلاحيةُ هي بالضبط ما يملكه
// `TenantAdmin` ولا يملكه `TenantStaff` (`RolePermissions`). ويومَ يطلب عميلٌ دوراً ماليّاً
// منفصلاً تصير صلاحيةً خاصّة؛ وذلك قرارُ أدوار لا قرارُ نقطة.
//
// **ولا قراءةَ لمسوّدة.** ما لم يصدر ليس مطالبةً بعد، ومسوّدةٌ أُلغيت لم تكن مطالبةً قطّ.
// ============================================================================
[ApiController]
[Route("api/admin/store/subscription")]
[Authorize]
[HasPermission(Permissions.Store.Settings)]
public class StoreSubscriptionController : ControllerBase
{
    private readonly IMediator _mediator;
    public StoreSubscriptionController(IMediator mediator) => _mediator = mediator;

    // الملخّص: الخطةُ وسعرُها، وما عليه، وكم فاتورةً مفتوحة ومتأخّرة، وتعليماتُ الدفع الحالية.
    [HttpGet]
    public async Task<IActionResult> Summary() => Ok(await _mediator.Send(new GetMySubscriptionQuery()));

    // GET /api/admin/store/subscription/invoices?status=&page=&pageSize=
    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListMyInvoicesQuery(status, page, pageSize)));

    [HttpGet("invoices/{id:int}")]
    public async Task<IActionResult> Invoice(int id) => this.ToHttp(await _mediator.Send(new GetMyInvoiceQuery(id)));
}
