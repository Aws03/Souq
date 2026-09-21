using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Souq.API.Security;
using Souq.API.Http;
using Souq.Application.Features.Customers;
using Souq.Application.Features.Customers.Account;

namespace Souq.API.Controllers;

// ============================================================================
// حساب العميل (المرحلة 7، /api/account): الملف، دفتر العناوين، تصدير بياناته، ومحو حسابه. العميل هو المستخدم الحالي
// دائماً (مطالبة cid) — حساب بلا ملف شراء (موظّف) ⇒ 403 CustomerAccountRequired، ومعرّف عنوان ليس في دفتره ⇒ 404.
// ============================================================================
[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IMediator _mediator;
    public AccountController(IMediator mediator) => _mediator = mediator;

    [HttpGet("profile")]
    public async Task<IActionResult> Profile() => this.ToHttp(await _mediator.Send(new GetMyProfileQuery()));

    // PUT /api/account/profile { "fullName": "…", "phone": "+962…" }
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateMyProfileCommand command) =>
        this.ToHttp(await _mediator.Send(command));

    [HttpGet("addresses")]
    public async Task<IActionResult> Addresses() => this.ToHttp(await _mediator.Send(new ListMyAddressesQuery()));

    // POST /api/account/addresses { "address": {…}, "defaultShipping": true } — 201 بالعنوان المحفوظ.
    [HttpPost("addresses")]
    public async Task<IActionResult> AddAddress([FromBody] AddMyAddressCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : this.Failure(result);
    }

    [HttpPut("addresses/{id:int}")]
    public async Task<IActionResult> UpdateAddress(int id, [FromBody] AddressInput body) =>
        this.ToHttp(await _mediator.Send(new UpdateMyAddressCommand(id, body)));

    [HttpDelete("addresses/{id:int}")]
    public async Task<IActionResult> RemoveAddress(int id) => this.ToHttp(await _mediator.Send(new RemoveMyAddressCommand(id)));

    [HttpPut("addresses/{id:int}/default-shipping")]
    public async Task<IActionResult> DefaultShipping(int id) =>
        this.ToHttp(await _mediator.Send(new SetMyDefaultAddressCommand(id, AddressUse.Shipping)));

    [HttpPut("addresses/{id:int}/default-billing")]
    public async Task<IActionResult> DefaultBilling(int id) =>
        this.ToHttp(await _mediator.Send(new SetMyDefaultAddressCommand(id, AddressUse.Billing)));

    // GET /api/account/export — ملف JSON بكل بيانات العميل (يُنزَّل مرفقاً).
    // محدود المعدّل (F-21): الاستجابة تكبر بعمر الحساب لا بصفحة، والقصُّ ليس خياراً — تصديرٌ
    // مبتور ليس تصديراً. فالقيد على التكرار: ستّ مرّات في الساعة.
    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Export()
    {
        var result = await _mediator.Send(new ExportMyDataQuery());
        if (!result.IsSuccess) return this.Failure(result);
        Response.Headers.ContentDisposition = "attachment; filename=\"account-data.json\"";
        return Ok(result.Value);
    }

    // POST /api/account/erase { "password": "…" } — محو لا رجعة فيه؛ الواجهة تخرج بعده (رمز التجديد أُلغي أصلاً).
    [HttpPost("erase")]
    public async Task<IActionResult> Erase([FromBody] EraseMyAccountCommand command) =>
        this.ToHttp(await _mediator.Send(command));
}
