using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
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
[AllowAnonymous] // مقصود: المفتاح العلني ليس سرّاً، والـ Webhook مُصادَق بتوقيع البوّابة لا بتوكن.
public class PaymentsController : ControllerBase
{
    private const int WebhookMaxBytes = 64 * 1024;

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
    // ── سقفٌ للجسم (M15) ──
    // هذه النقطة مجهولة الهوية بالضرورة (الشبكة تستدعيها لا مستخدم)، ولا حدّ معدّل عليها، و**تقرأ
    // الجسم كاملاً إلى نصّ قبل التحقّق من التوقيع** — ثمّ يقرأ الموجّه حساب المتجر ويفكّ تشفير مفتاحه
    // بـ AES-GCM ويحسب HMAC على ما وصل. فجسمٌ بحجم ثلاثين ميغابايت (سقف Kestrel الافتراضي، وnginx
    // يمرّر حتى خمسة وخمسين) يكلّف كل ذلك قبل أن يُرفض. وأحداث Stripe الحقيقية دون 64 كيلوبايت
    // بمراتب، فالسقف لا يمنع حدثاً صحيحاً واحداً.
    //
    // كل نقطة أخرى تقبل جسماً في هذه الواجهة لها `[RequestSizeLimit]` صريح؛ هذه وحدها كانت بلا سقف.
    [HttpPost("webhook")]
    [RequestSizeLimit(WebhookMaxBytes)]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        var result = await _mediator.Send(
            new ProcessPaymentWebhookCommand(payload, Request.Headers["Stripe-Signature"].ToString()));
        return result.IsSuccess ? Ok() : this.Failure(result);
    }
}
