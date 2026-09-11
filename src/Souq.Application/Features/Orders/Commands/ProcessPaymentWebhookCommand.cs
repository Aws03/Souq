using MediatR;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// ProcessPaymentWebhookCommand — حالة استخدام في وحدة Ordering (لا Payments): وحدة
// الطلبات تعتمد على عقد الدفع، لا العكس (Modules.md — لا دورات). البوّابة تتحقّق من
// التوقيع وتستخرج مرجع الطلب؛ ثم نمرّر لنفس أمر التأكيد الذي يستدعيه العميل — يعيد
// التحقّق من الحالة لدى البوّابة نفسها ومضمون التكرار (Idempotent).
// ============================================================================
public record ProcessPaymentWebhookCommand(string Payload, string? Signature) : IRequest<Result>;

public class ProcessPaymentWebhookHandler : IRequestHandler<ProcessPaymentWebhookCommand, Result>
{
    private readonly IPaymentService _payment;
    private readonly ISender _sender;

    public ProcessPaymentWebhookHandler(IPaymentService payment, ISender sender)
    {
        _payment = payment; _sender = sender;
    }

    public async Task<Result> Handle(ProcessPaymentWebhookCommand cmd, CancellationToken ct)
    {
        PaymentWebhookEvent? evt;
        try { evt = _payment.ParseWebhook(cmd.Payload, cmd.Signature); }
        catch (InvalidPaymentWebhookException ex) { return Result.Failure(ex.Message, "InvalidSignature"); }

        // حدث لا يخصّ دفعة طلب (أو لا Webhook مضبوط) — نُقرّ بالاستلام دون فعل شيء.
        if (evt is null || !int.TryParse(evt.OrderReference, out var orderId))
            return Result.Success();

        // نتيجة التأكيد (نجاح/فشل دفع) تُطبَّق على الطلب؛ للبوّابة يكفي أننا عالجنا الحدث.
        await _sender.Send(new ConfirmOrderPaymentCommand(orderId), ct);
        return Result.Success();
    }
}
