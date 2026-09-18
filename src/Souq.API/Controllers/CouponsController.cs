using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Souq.API.Http;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Domain.Platform;
using Souq.Application.Common.Security;
using Souq.Application.Features.Coupons.Commands;
using Souq.Application.Features.Coupons.Queries;

namespace Souq.API.Controllers;

// وحدة Promotions اختيارية (D-11): معطّلة للمتجر ⇒ كل نقاطها 404 ModuleDisabled — قبل الاستيثاق، فزائرٌ يطلبها
// يقرأ "الوحدة معطّلة" لا "سجّل دخولك" (TenantAvailability قبل UseAuthentication في Program.cs).
//
// **كل ما هنا إدارة (M8).** كانت هنا نقطة عامة سادسة، `GET apply`، تُسعّر كوبوناً على إجمالي فرعي يرسله
// العميل — تقييمٌ ثانٍ للكوبون خارج خطّ التسعير. حُذفت في M8 (TD-06): `GET /api/basket/quote` يفعل الشيء
// نفسه على السلة الحقيقية، بلا استيثاق وبحدّ المعدّل نفسه، فلا يستطيع أن يخالف ما يقوله الدفع.
[ApiController]
[Route("api/[controller]")]
[RequiresModule(StoreModules.Promotions)]
public class CouponsController : ControllerBase
{
    private readonly IMediator _mediator;
    public CouponsController(IMediator mediator) => _mediator = mediator;

    // GET /api/coupons — كل الكوبونات مرقّمة (نشطة ومعطّلة معاً).
    [HttpGet]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new GetCouponsQuery(page, pageSize)));

    // POST /api/coupons — ينشئ كوبوناً.
    [HttpPost]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateCouponCommand command)
    {
        var result = await _mediator.Send(command);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, new { id = result.Value }) : this.Failure(result);
    }

    // PUT /api/coupons/5 — يفرض معرّف المسار على الأمر.
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCouponCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }

    // GET /api/coupons/5/redemptions — من استخدم الكوبون ولأيّ طلب وبأيّ حالة (المرحلة 10). كوبون متجر آخر ⇒ 404.
    [HttpGet("{id:int}/redemptions")]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> Redemptions(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetCouponRedemptionsQuery(id, page, pageSize));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // DELETE /api/coupons/5 — حذف فعلي قبل أي استخدام فقط؛ بعده 409 CouponInUse (يُعطَّل بدلاً من ذلك).
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Promotions.Manage)]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _mediator.Send(new DeleteCouponCommand(id));
        return result.IsSuccess ? NoContent() : this.Failure(result);
    }
}
