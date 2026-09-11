using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Shipping;

namespace Souq.API.Controllers;

// ============================================================================
// طرق الشحن من إدارة المتجر (المرحلة 12، store.shipping.manage). المتجر متجر المضيف: معرّف طريقة متجر آخر ⇒ 404. العميل
// يرى الطرق المتاحة لعنوانه مع تسعير السلة (/api/basket/quote) لا هنا.
// ============================================================================
[ApiController]
[Route("api/admin/shipping-methods")]
[HasPermission(Permissions.Store.Shipping)]
public class ShippingMethodsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ShippingMethodsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _mediator.Send(new ListShippingMethodsQuery()));

    // POST /api/admin/shipping-methods { "name": "توصيل عادي", "price": 2.5, "freeOverAmount": 50, "minDays": 1, "maxDays": 3,
    //   "carrier": "Aramex", "trackingUrlTemplate": "https://…/{number}", "countries": ["JO"] }
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ShippingMethodInput body)
    {
        var result = await _mediator.Send(new CreateShippingMethodCommand(body));
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, new { id = result.Value }) : this.Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ShippingMethodInput body)
        => this.ToHttp(await _mediator.Send(new UpdateShippingMethodCommand(id, body)));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) => this.ToHttp(await _mediator.Send(new DeleteShippingMethodCommand(id)));
}
