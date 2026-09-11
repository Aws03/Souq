using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// ProcessPaymentWebhookCommand — حالة استخدام في وحدة Ordering (لا Payments): وحدة
// الطلبات تعتمد على عقد الدفع، لا العكس (Modules.md — لا دورات). البوّابة تتحقّق من
// التوقيع وتستخرج مرجع الطلب؛ ثم نفس منطق التأكيد الذي يستخدمه العميل (OrderPaymentConfirmation)
// — يعيد التحقّق لدى البوّابة ومضمون التكرار. التفويض هنا هو التوقيع لا مستخدم.
// ============================================================================
public record ProcessPaymentWebhookCommand(string Payload, string? Signature) : IRequest<Result>;

public class ProcessPaymentWebhookHandler : IRequestHandler<ProcessPaymentWebhookCommand, Result>
{
    private readonly IPaymentService _payment;
    private readonly IOrderRepository _orders;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly ILogger<ProcessPaymentWebhookHandler> _logger;

    public ProcessPaymentWebhookHandler(
        IPaymentService payment, IOrderRepository orders, OrderPaymentConfirmation confirmation,
        ILogger<ProcessPaymentWebhookHandler> logger)
    {
        _payment = payment; _orders = orders; _confirmation = confirmation; _logger = logger;
    }

    public async Task<Result> Handle(ProcessPaymentWebhookCommand cmd, CancellationToken ct)
    {
        PaymentWebhookEvent? evt;
        try { evt = _payment.ParseWebhook(cmd.Payload, cmd.Signature); }
        catch (InvalidPaymentWebhookException ex) { return Result.Failure(Error.Validation("InvalidSignature", ex.Message)); }

        // حدث لا يخصّ دفعة طلب (أو لا Webhook مضبوط) — نُقرّ بالاستلام دون فعل شيء.
        if (evt is null || !int.TryParse(evt.OrderReference, out var orderId))
            return Result.Success();

        var order = await _orders.GetWithItemsAsync(orderId, ct);
        if (order is null)
        {
            _logger.LogWarning("Payment webhook referenced unknown order {OrderId}; acknowledged without action", orderId);
            return Result.Success();
        }

        // نتيجة التأكيد (نجاح/فشل دفع) تُطبَّق على الطلب؛ للبوّابة يكفي أننا عالجنا الحدث.
        await _confirmation.ConfirmAsync(order, ct);
        return Result.Success();
    }
}
