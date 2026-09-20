using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Security;
using Souq.Application.Features.Billing;

namespace Souq.API.Controllers;

// ============================================================================
// مستوى التحكّم التجاري (C1، ADR-0047): كتالوج الخطط، وما يسري منها على كل متجر، واستثناءات الدعم.
// على مضيف المنصّة وحده ([PlatformEndpoint])، وبصلاحية المالك (platform.billing.manage) — العلاقة
// التجارية مع العميل ليست تشغيل متجره اليومي.
//
// لا مالَ هنا: لا أسعار ولا فواتير ولا تحصيل. مسار مال المتسوّق (وحدة Payments) لا يلتقي بهذا
// المسار إطلاقاً، وفوترة التاجر نفسها مؤجّلة إلى C5 وما يسبقها من قرارات مالك.
// ============================================================================
[ApiController]
[Route("api/platform/plans")]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Billing)]
public class PlatformPlansController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformPlansController(IMediator mediator) => _mediator = mediator;

    // GET /api/platform/plans?search=&includeRetired=false&page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] bool includeRetired = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListPlansQuery(search, includeRetired, page, pageSize)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => this.ToHttp(await _mediator.Send(new GetPlanQuery(id)));

    // إصدار جديد من خطة (الأول = 1). يبدأ مسوّدةً: لا يُشترَك عليه حتى يُنشر.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlanVersionRequest body)
    {
        var result = await _mediator.Send(new CreatePlanVersionCommand(
            body.Code, body.Name, body.Entitlements!, body.Limits!));
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, new { id = result.Value })
            : this.Failure(result);
    }

    // تحرير المسوّدة وحدها — الشروط المشترَك عليها لا تتغيّر (يرفضه المجال بـ 422).
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePlanDraftRequest body)
        => this.ToHttp(await _mediator.Send(new UpdatePlanDraftCommand(id, body.Name, body.Entitlements!, body.Limits!)));

    [HttpPost("{id:int}/publish")]
    public async Task<IActionResult> Publish(int id) => this.ToHttp(await _mediator.Send(new PublishPlanCommand(id)));

    [HttpPost("{id:int}/retire")]
    public async Task<IActionResult> Retire(int id) => this.ToHttp(await _mediator.Send(new RetirePlanCommand(id)));
}

// ============================================================================
// استحقاقات متجر بعينه. المسار يحمل معرّف المتجر — امتياز منطقة المنصّة وحدها، ويثبته
// `كل_مجلّد_في_منطقة_المنصّة_يُخدَم_على_مضيفها_وحده` لا مجرّد إعلانٍ في تعليق.
// ============================================================================
[ApiController]
[Route("api/platform/tenants/{tenantId:int}")]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Billing)]
public class PlatformTenantBillingController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformTenantBillingController(IMediator mediator) => _mediator = mediator;

    // الجواب الواحد مشروحاً: ما يسري فعلاً، ومن أين جاء كل بند.
    [HttpGet("entitlements")]
    public async Task<IActionResult> Entitlements(int tenantId)
        => this.ToHttp(await _mediator.Send(new GetTenantEntitlementsQuery(tenantId)));

    [HttpPut("plan")]
    public async Task<IActionResult> AssignPlan(int tenantId, [FromBody] AssignPlanRequest body)
        => this.ToHttp(await _mediator.Send(new AssignTenantPlanCommand(tenantId, body.PlanId!.Value)));

    // يُنهي ما يمنحه العقد؛ لا يوقف المتجر (الإيقاف حالةٌ أخرى بمسارها الخاص).
    [HttpDelete("plan")]
    public async Task<IActionResult> CancelPlan(int tenantId)
        => this.ToHttp(await _mediator.Send(new CancelTenantPlanCommand(tenantId)));

    [HttpGet("entitlement-overrides")]
    public async Task<IActionResult> Overrides(int tenantId)
        => this.ToHttp(await _mediator.Send(new ListEntitlementOverridesQuery(tenantId)));

    [HttpPost("entitlement-overrides")]
    public async Task<IActionResult> GrantOverride(int tenantId, [FromBody] GrantOverrideRequest body)
    {
        var result = await _mediator.Send(new GrantEntitlementOverrideCommand(
            tenantId, body.Entitlement, body.Days!.Value, body.Reason));
        return result.IsSuccess
            ? CreatedAtAction(nameof(Overrides), new { tenantId }, new { id = result.Value })
            : this.Failure(result);
    }

    [HttpDelete("entitlement-overrides/{overrideId:int}")]
    public async Task<IActionResult> RevokeOverride(int tenantId, int overrideId)
        => this.ToHttp(await _mediator.Send(new RevokeEntitlementOverrideCommand(tenantId, overrideId)));
}

// الحقول المطلوبة قابلة للعدم و[Required] عمداً (كما في UpdateOrderStatusRequest وTenantModulesRequest):
// بأنواع غير قابلة للعدم كان الجسم الفارغ {} يُقبل ويعني العضو صفر — خطةً معرّفها 0، أو استثناءً مدّته
// صفر يوم. "غائب" يجب أن يُميَّز عن "العضو صفر" لا أن يساويه.
// Entitlements/Limits: [Required] ترفض الغياب وحده — القائمة الفارغة صراحةً خطةٌ مشروعة لا تمنح شيئاً.
public record CreatePlanVersionRequest(
    string Code, string Name,
    [Required] IReadOnlyList<string>? Entitlements,
    [Required] IReadOnlyList<PlanLimitDto>? Limits);

public record UpdatePlanDraftRequest(
    string Name,
    [Required] IReadOnlyList<string>? Entitlements,
    [Required] IReadOnlyList<PlanLimitDto>? Limits);

public record AssignPlanRequest([Required] int? PlanId);

public record GrantOverrideRequest(string Entitlement, [Required] int? Days, string Reason);
