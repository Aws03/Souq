using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Customers.Admin;
using Souq.Domain.Enums;

namespace Souq.API.Controllers;

// ============================================================================
// عملاء المتجر للإدارة (المرحلة 7): القائمة والتفاصيل بـ customers.view، والحظر والتصدير والمحو بـ customers.manage أيضاً
// (كلها مُدقَّقة). سجلّ طلبات العميل من GET /api/orders?customerId= (وحدة Ordering). معرّف عميل متجر آخر ⇒ 404.
// ============================================================================
[ApiController]
[Route("api/admin/customers")]
[HasPermission(Permissions.Customers.View)]
public class AdminCustomersController : ControllerBase
{
    private readonly IMediator _mediator;
    public AdminCustomersController(IMediator mediator) => _mediator = mediator;

    // GET /api/admin/customers?keyword=&status=Blocked&page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? keyword, [FromQuery] CustomerStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _mediator.Send(new ListCustomersQuery(keyword, status, page, pageSize)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => this.ToHttp(await _mediator.Send(new GetCustomerQuery(id)));

    // PUT /api/admin/customers/5/status { "status": "Blocked" | "Active" }
    [HttpPut("{id:int}/status")]
    [HasPermission(Permissions.Customers.Manage)]
    public async Task<IActionResult> SetStatus(int id, [FromBody] CustomerStatusRequest body) =>
        this.ToHttp(await _mediator.Send(new SetCustomerStatusCommand(id, body.Status)));

    [HttpGet("{id:int}/export")]
    [HasPermission(Permissions.Customers.Manage)]
    public async Task<IActionResult> Export(int id)
    {
        var result = await _mediator.Send(new ExportCustomerDataQuery(id));
        if (!result.IsSuccess) return this.Failure(result);
        Response.Headers.ContentDisposition = $"attachment; filename=\"customer-{id}-data.json\"";
        return Ok(result.Value);
    }

    [HttpPost("{id:int}/erase")]
    [HasPermission(Permissions.Customers.Manage)]
    public async Task<IActionResult> Erase(int id) => this.ToHttp(await _mediator.Send(new EraseCustomerCommand(id)));
}

public record CustomerStatusRequest(CustomerStatus Status);
