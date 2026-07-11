using MediatR;
using Microsoft.AspNetCore.Mvc;
using Souq.Application.Features.Orders.Commands;
using Stripe;

namespace Souq.API.Controllers;

// ============================================================================
// PaymentsController — نقطتان عامّتان (بلا مصادقة توكن) لأسباب مختلفة تماماً:
//   /config: تُعطي الواجهة مفتاح Stripe العلني (ليس سرّاً، Stripe.js يحتاجه).
//   /webhook: يستدعيه خادم Stripe نفسه مباشرة — أمانه بتوقيع Stripe الرقمي
//   (Stripe-Signature) لا بتوكن JWT، فلا معنى لحمايته بـ [Authorize].
// ============================================================================
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _config;

    public PaymentsController(IMediator mediator, IConfiguration config)
    {
        _mediator = mediator; _config = config;
    }

    // GET /api/payments/config — تهيئة Stripe.js في الواجهة.
    [HttpGet("config")]
    public IActionResult GetConfig() =>
        Ok(new { publishableKey = _config["Stripe:PublishableKey"] ?? "" });

    // POST /api/payments/webhook — دفاع في العمق: يضمن تأكيد الطلب حتى لو أغلق
    // العميل متصفّحه قبل استدعاء /orders/{id}/confirm-payment بنفسه.
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
            return Ok(); // لا Stripe حقيقياً مضبوطاً (تطوير محلي) — لا شيء لنتحقّق منه.

        var json = await new StreamReader(Request.Body).ReadToEndAsync();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"].ToString(), webhookSecret);
        }
        catch (StripeException)
        {
            return BadRequest(); // توقيع غير صالح — قد يكون طلباً مزيّفاً.
        }

        if ((stripeEvent.Type == "payment_intent.succeeded" || stripeEvent.Type == "payment_intent.payment_failed")
            && stripeEvent.Data.Object is PaymentIntent intent
            && intent.Metadata.TryGetValue("orderReference", out var reference)
            && int.TryParse(reference, out var orderId))
        {
            // نفس الأمر الذي يستدعيه العميل — يعيد التحقّق من الحالة لدى Stripe
            // نفسها بدل الثقة بحمولة الـ Webhook مباشرة، ومضمون التكرار (Idempotent).
            await _mediator.Send(new ConfirmOrderPaymentCommand(orderId));
        }

        return Ok();
    }
}
