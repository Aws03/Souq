using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Payments;
using Souq.Application.Features.Payments.Contracts;
using Souq.Application.Features.Staff;
using Souq.Application.Features.Stores;
using Souq.Domain.Platform;

namespace Souq.API.Controllers;

// ============================================================================
// إدارة المتجر من داخله (المرحلة 4): إعدادات الواجهة وملفات الهوية (store.settings.manage) وموظّفو المتجر
// (store.staff.manage). لا معرّف متجر في أي مسار هنا — المتجر هو متجر المضيف دائماً. ثلاث نقاط رفع صريحة
// بدل مسار بمعامل: كل مسار بمعامل مورد يدخل جدول اختبار العزل.
// ============================================================================
[ApiController]
[Route("api/admin/store")]
[HasPermission(Permissions.Store.Settings)]
public class StoreSettingsController : ControllerBase
{
    private const long MaxUploadBytes = 3 * 1024 * 1024;

    private readonly IMediator _mediator;
    public StoreSettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("settings")]
    public async Task<IActionResult> Get() => Ok(await _mediator.Send(new GetStoreSettingsQuery()));

    // القوائم والحدود التي يقبلها الخادم — كي لا تحمل الواجهة نسخة منها تفترق.
    [HttpGet("settings/options")]
    public async Task<IActionResult> Options() => Ok(await _mediator.Send(new GetStoreSettingsOptionsQuery()));

    [HttpPut("settings")]
    public async Task<IActionResult> Update([FromBody] StoreSettingsInput settings)
        => this.ToHttp(await _mediator.Send(new UpdateStoreSettingsCommand(settings)));

    [HttpPost("branding/logo")]
    [RequestSizeLimit(MaxUploadBytes)]
    public Task<IActionResult> UploadLogo(IFormFile file) => Upload(BrandingAsset.Logo, file);

    [HttpPost("branding/favicon")]
    [RequestSizeLimit(MaxUploadBytes)]
    public Task<IActionResult> UploadFavicon(IFormFile file) => Upload(BrandingAsset.Favicon, file);

    [HttpPost("branding/social-image")]
    [RequestSizeLimit(MaxUploadBytes)]
    public Task<IActionResult> UploadSocialImage(IFormFile file) => Upload(BrandingAsset.SocialImage, file);

    private async Task<IActionResult> Upload(BrandingAsset asset, IFormFile file)
    {
        if (file is null || file.Length == 0)
            return this.Failure(Error.Validation("FileRequired", "لم يُرفق ملف"));

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadStoreBrandingCommand(asset, stream, file.Length));
        return result.IsSuccess ? Ok(new { url = result.Value }) : this.Failure(result);
    }
}

// حساب بوّابة الدفع الخاص بالمتجر (المرحلة 11، store.payments.manage): بلا معرّف في المسار، والسرّان يُكتبان ولا يُقرآن —
// GET يعيد التلميح وهل سرّ الإشعارات مضبوط فقط. DELETE يعيد المتجر لحساب النشر.
[ApiController]
[Route("api/admin/store/payments")]
[HasPermission(Permissions.Store.Payments)]
public class StorePaymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    public StorePaymentsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _mediator.Send(new GetStorePaymentAccountQuery()));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] StorePaymentAccountInput account)
        => this.ToHttp(await _mediator.Send(new UpdateStorePaymentAccountCommand(account)));

    [HttpDelete]
    public async Task<IActionResult> Remove() => this.ToHttp(await _mediator.Send(new RemoveStorePaymentAccountCommand()));
}

[ApiController]
[Route("api/admin/staff")]
[HasPermission(Permissions.Store.Staff)]
public class StaffController : ControllerBase
{
    private readonly IMediator _mediator;
    public StaffController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListStaffQuery(page, pageSize)));

    // { "fullName": "...", "email": "...", "role": "TenantAdmin" | "TenantStaff" } — رابط القبول على هذا المضيف.
    [HttpPost]
    public async Task<IActionResult> Invite([FromBody] InviteStaffCommand command)
        => this.ToHttp(await _mediator.Send(command));

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] AccountStatusRequest body)
        => this.ToHttp(await _mediator.Send(new SetStaffStatusCommand(id, body.Active!.Value)));
}

// ============================================================================
// إعداد الواجهة العام (D-12) — أول ما تطلبه الواجهة على أي مضيف متجر: الهوية واللغة والعملة والوحدات. ETag من
// المحتوى نفسه: المتصفّح يتحقّق بـ If-None-Match فيأخذ 304 بلا جسم ما لم يتغيّر شيء (no-cache = تحقّق كل مرّة،
// لا نسخة قديمة بعد تعديل الهوية). متجر موقوف ⇒ 503 StoreUnavailable كبقية النقاط (الواجهة تعرض صفحة الإغلاق).
// ============================================================================
[ApiController]
[Route("api/storefront")]
public class StorefrontController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly JsonSerializerOptions _json;

    public StorefrontController(IMediator mediator, IOptions<JsonOptions> json)
    {
        _mediator = mediator; _json = json.Value.JsonSerializerOptions;
    }

    // متاحة والمتجر مغلق (موقوف/مؤرشف/قيد التجهيز): هذه أول ما تطلبه الواجهة، وبها وحدها تعرض صفحة "المتجر غير متاح"
    // بهويّة المتجر بدل صفحة خطأ عارية. لا تكشف إلا بيانات العرض (R-08).
    [HttpGet("config")]
    [AllowAnonymous]
    [AvailableWhenStoreClosed]
    public async Task<IActionResult> Config()
    {
        var config = await _mediator.Send(new GetStorefrontConfigQuery());
        var body = JsonSerializer.SerializeToUtf8Bytes(config, _json);
        var etag = $"\"{Convert.ToHexString(SHA256.HashData(body))[..32]}\"";

        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "no-cache";
        var cached = Request.Headers.IfNoneMatch.SelectMany(v => (v ?? "").Split(',')).Any(v => v.Trim() == etag);
        return cached ? StatusCode(StatusCodes.Status304NotModified) : File(body, "application/json");
    }
}
