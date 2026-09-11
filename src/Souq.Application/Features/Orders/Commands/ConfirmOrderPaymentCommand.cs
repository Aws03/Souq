using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// يستدعيه العميل بعد مصادقته على الدفع مع Stripe من متصفّحه. لا نثق بادّعائه:
// OrderPaymentConfirmation يتحقّق من نيّة الدفع لدى البوّابة نفسها. الملكية تُفحص هنا
// في Application (لا في الـ Controller — Phase 0 B7): عميل يؤكّد طلب غيره كان سيُلغيه إن
// لم يكتمل دفعه بعد. مسار الـ Webhook (بلا مستخدم) في ProcessPaymentWebhookCommand.
// ============================================================================
public record ConfirmOrderPaymentCommand(int OrderId) : IRequest<Result<OrderConfirmedDto>>;

public class ConfirmOrderPaymentHandler : IRequestHandler<ConfirmOrderPaymentCommand, Result<OrderConfirmedDto>>
{
    private readonly IOrderRepository _orders;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly ICurrentUser _currentUser;

    public ConfirmOrderPaymentHandler(IOrderRepository orders, OrderPaymentConfirmation confirmation, ICurrentUser currentUser)
    {
        _orders = orders; _confirmation = confirmation; _currentUser = currentUser;
    }

    public async Task<Result<OrderConfirmedDto>> Handle(ConfirmOrderPaymentCommand cmd, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(cmd.OrderId, ct);

        // غير موجود أو يخصّ غيرك ⇒ الجواب نفسه (لا نكشف وجود طلبات الآخرين).
        if (order is null || !_currentUser.CanAccessOwnedBy(order.CustomerId, Permissions.Orders.Manage))
            return Result<OrderConfirmedDto>.Failure(Error.NotFound("الطلب غير موجود"));

        return await _confirmation.ConfirmAsync(order, ct);
    }
}
