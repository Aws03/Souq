using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Souq.API.Http;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Features.Payments.Commands;

namespace Souq.API.Controllers;

// ============================================================================
// استرداد دفعة طلب (المرحلة 11، store.payments.manage). المتجر متجر المضيف: طلب متجر آخر لا دفعة له هنا ⇒ 404. الردّ
// نتيجة الاسترداد: Succeeded، أو Failed بسبب البوّابة، أو Pending حين لم تُجب (يُعاد بـ retry بالمفتاح نفسه).
// ============================================================================
[ApiController]
[Route("api/orders/{id:int}/refunds")]
[HasPermission(Permissions.Store.Payments)]
public class OrderRefundsController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrderRefundsController(IMediator mediator) => _mediator = mediator;

    // POST /api/orders/5/refunds { "amount": 10.5, "reason": "…" } — بلا amount ⇒ كل المتبقّي.
    [HttpPost]
    public async Task<IActionResult> Refund(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefundRequest? body)
    {
        var result = await _mediator.Send(new RefundOrderCommand(id, body?.Amount, body?.Reason));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }

    // POST /api/orders/5/refunds/12/retry — لاسترداد معلّق فقط.
    [HttpPost("{refundId:int}/retry")]
    public async Task<IActionResult> Retry(int id, int refundId)
    {
        var result = await _mediator.Send(new RetryRefundCommand(id, refundId));
        return result.IsSuccess ? Ok(result.Value) : this.Failure(result);
    }
}

public record RefundRequest(decimal? Amount = null, string? Reason = null);
