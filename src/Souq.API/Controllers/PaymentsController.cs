using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Features.Payments.Queries;

namespace Souq.API.Controllers;

// ============================================================================
// PaymentsController — نقطتان عامّتان (بلا مصادقة توكن) لأسباب مختلفة:
//   /config: تُعطي الواجهة مفتاح مزوّد الدفع العلني (ليس سرّاً).
//   /webhook: يستدعيه خادم Stripe مباشرة — أمانه بالتوقيع الرقمي لا بتوكن JWT.
// الـ Controller لا يعرف Stripe إطلاقاً (لا using Stripe): يمرّر الجسم والتوقيع
// لحالة استخدام تتحقّق عبر منفذ الدفع (ADR-0003, Phase 0 D1).
// ============================================================================
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    public PaymentsController(IMediator mediator) => _mediator = mediator;

    // GET /api/payments/config — تهيئة Stripe.js في الواجهة (مفتاح فارغ ⇒ بوّابة تجريبية).
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        var config = await _mediator.Send(new GetPaymentConfigQuery());
        return Ok(new { publishableKey = config.PublishableKey ?? "" });
    }

    // POST /api/payments/webhook — دفاع في العمق: يضمن تأكيد الطلب حتى لو أغلق
    // العميل متصفّحه قبل استدعاء /orders/{id}/confirm-payment بنفسه.
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        var result = await _mediator.Send(
            new ProcessPaymentWebhookCommand(payload, Request.Headers["Stripe-Signature"].ToString()));
        return result.IsSuccess ? Ok() : BadRequest();
    }
}
