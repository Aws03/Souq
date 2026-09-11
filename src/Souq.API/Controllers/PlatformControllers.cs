using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Platform;
using Souq.Application.Features.Reporting;
using Souq.Application.Features.Stores;
using Souq.Domain.Platform;

namespace Souq.API.Controllers;

// ============================================================================
// منطقة المنصّة (المرحلة 4): على مضيف المنصّة وحده ([PlatformEndpoint] ⇒ 404 على مضيف متجر، وتوكن متجر ⇒ 401
// على مضيف المنصّة)، وبصلاحيات المنصّة. هنا وحده يأتي معرّف متجر من المسار، وكل طلب مُدقَّق (سجلّ التدقيق).
// الواجهة الرسومية في المرحلة 18؛ حتى ذلك الحين عبر Swagger/HTTP.
// ============================================================================
[ApiController]
[Route("api/platform/tenants")]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Tenants)]
public class PlatformTenantsController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformTenantsController(IMediator mediator) => _mediator = mediator;

    // GET /api/platform/tenants?search=&status=Active&page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] TenantStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListTenantsQuery(search, status, page, pageSize)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => this.ToHttp(await _mediator.Send(new GetTenantQuery(id)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value }, new { id = result.Value })
            : this.Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTenantRequest body)
        => this.ToHttp(await _mediator.Send(new UpdateTenantCommand(id, body.Name, body.Currency)));

    // { "action": "Activate" | "Suspend" | "Archive" }
    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] TenantStatusRequest body)
        => this.ToHttp(await _mediator.Send(new ChangeTenantStatusCommand(id, body.Action)));

    [HttpPost("{id:int}/domains")]
    public async Task<IActionResult> AddDomain(int id, [FromBody] TenantDomainRequest body)
        => this.ToHttp(await _mediator.Send(new ChangeTenantDomainCommand(id, body.Host, TenantDomainAction.Add)));

    [HttpDelete("{id:int}/domains/{host}")]
    public async Task<IActionResult> RemoveDomain(int id, string host)
        => this.ToHttp(await _mediator.Send(new ChangeTenantDomainCommand(id, host, TenantDomainAction.Remove)));

    [HttpPost("{id:int}/domains/{host}/primary")]
    public async Task<IActionResult> SetPrimaryDomain(int id, string host)
        => this.ToHttp(await _mediator.Send(new ChangeTenantDomainCommand(id, host, TenantDomainAction.SetPrimary)));

    // تأكيد يدوي من المنصّة بعد ضبط DNS (فحص تلقائي في المرحلة 23).
    [HttpPost("{id:int}/domains/{host}/verify")]
    public async Task<IActionResult> VerifyDomain(int id, string host)
        => this.ToHttp(await _mediator.Send(new ChangeTenantDomainCommand(id, host, TenantDomainAction.Verify)));

    [HttpPut("{id:int}/settings")]
    public async Task<IActionResult> UpdateSettings(int id, [FromBody] StoreSettingsInput settings)
        => this.ToHttp(await _mediator.Send(new UpdateTenantSettingsCommand(id, settings)));

    // { "modules": ["promotions", "reviews", "wishlist"] } — القائمة كاملة تستبدل الحالية.
    [HttpPut("{id:int}/modules")]
    public async Task<IActionResult> SetModules(int id, [FromBody] TenantModulesRequest body)
        => this.ToHttp(await _mediator.Send(new SetTenantModulesCommand(id, body.Modules ?? [])));

    // POST /api/platform/tenants/5/branding/Logo|Favicon|SocialImage — multipart؛ النوع من المحتوى (ADR-0016).
    [HttpPost("{id:int}/branding/{asset}")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadBranding(int id, BrandingAsset asset, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.Failure(Error.Validation("FileRequired", "لم يُرفق ملف"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadTenantBrandingCommand(id, asset, stream, file.Length));
        return result.IsSuccess ? Ok(new { url = result.Value }) : this.Failure(result);
    }

    [HttpGet("{id:int}/accounts")]
    public async Task<IActionResult> ListAccounts(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => this.ToHttp(await _mediator.Send(new ListTenantAccountsQuery(id, page, pageSize)));

    // دعوة مدير المتجر: الرابط على نطاق المتجر الأساسي (يلزم نطاق مضاف).
    [HttpPost("{id:int}/admins")]
    public async Task<IActionResult> InviteAdmin(int id, [FromBody] InviteAccountRequest body)
        => this.ToHttp(await _mediator.Send(new InviteTenantAdminCommand(id, body.FullName, body.Email)));
}

// حسابات المنصّة — للمالك وحده (platform.users.manage).
[ApiController]
[Route("api/platform/users")]
[PlatformEndpoint]
[HasPermission(Permissions.Platform.Users)]
public class PlatformUsersController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformUsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListPlatformUsersQuery(page, pageSize)));

    [HttpPost]
    public async Task<IActionResult> Invite([FromBody] InvitePlatformUserCommand command)
        => this.ToHttp(await _mediator.Send(command));

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] AccountStatusRequest body)
        => this.ToHttp(await _mediator.Send(new SetPlatformUserStatusCommand(id, body.Active)));
}

// الإحصاءات (platform.reports.view) وسجلّ التدقيق (platform.audit.view).
[ApiController]
[Route("api/platform")]
[PlatformEndpoint]
public class PlatformInsightsController : ControllerBase
{
    private readonly IMediator _mediator;
    public PlatformInsightsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("stats")]
    [HasPermission(Permissions.Platform.Reports)]
    public async Task<IActionResult> Stats() => Ok(await _mediator.Send(new GetPlatformStatsQuery()));

    // GET /api/platform/audit?tenantId=&action=tenant.&actorUserId=&from=&to=&page=1&pageSize=50
    [HttpGet("audit")]
    [HasPermission(Permissions.Platform.Audit)]
    public async Task<IActionResult> Audit(
        [FromQuery] int? tenantId, [FromQuery] string? action, [FromQuery] int? actorUserId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _mediator.Send(new ListAuditEntriesQuery(tenantId, action, actorUserId, from, to, page, pageSize)));
}

public record UpdateTenantRequest(string Name, string Currency);
public record TenantStatusRequest(TenantLifecycleAction Action);
public record TenantDomainRequest(string Host);
public record TenantModulesRequest(IReadOnlyList<string>? Modules);
public record InviteAccountRequest(string FullName, string Email);
public record AccountStatusRequest(bool Active);
