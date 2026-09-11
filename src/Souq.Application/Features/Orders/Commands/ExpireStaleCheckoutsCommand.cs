using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// منسّق انتهاء مهلة الدفع (المرحلة 6، يشغّله خادم خلفي لكل متجر — D-15). يغلق بقيّة Phase 0 C6: طلب Pending هُجر
// (أُغلقت الصفحة، انقطعت البوّابة، تعطّل الخادم بين الحفظين) كان يحجز المخزون للأبد. لكل مرجع حجز انتهت مهلته:
//   • طلب Pending بلا نيّة دفع ⇒ يُلغى ويُحرَّر حجزه.
//   • طلب Pending بنيّة دفع ⇒ نطلب من البوّابة إلغاءها أولاً (خارج أي معاملة): نجحت قبل الإلغاء ⇒ تأكيد الدفع كالمعتاد؛
//     أُلغيت ⇒ يُلغى الطلب؛ ما زالت قيد المعالجة ⇒ تُترك للدورة التالية. لا يُلغى طلب قد يكون دُفع.
//   • حجز لطلب غير Pending أو غير موجود ⇒ تسوية دفاعية (المدفوع يُلتزم، الملغى أو المفقود يُحرَّر).
// كل مرجع مستقلّ: فشل واحد يُسجَّل ولا يوقف البقية، ويُعاد في الدورة التالية.
// ============================================================================
public record ExpireStaleCheckoutsCommand(int Max = 50) : IRequest<int>;

public class ExpireStaleCheckoutsHandler : IRequestHandler<ExpireStaleCheckoutsCommand, int>
{
    private const string ExpiredNote = "انتهت مهلة الدفع";

    private readonly IInventoryReservations _reservations;
    private readonly IOrderRepository _orders;
    private readonly IPaymentService _payment;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly ILogger<ExpireStaleCheckoutsHandler> _logger;

    public ExpireStaleCheckoutsHandler(
        IInventoryReservations reservations, IOrderRepository orders, IPaymentService payment,
        OrderPaymentConfirmation confirmation, ILogger<ExpireStaleCheckoutsHandler> logger)
    {
        _reservations = reservations; _orders = orders; _payment = payment; _confirmation = confirmation; _logger = logger;
    }

    public async Task<int> Handle(ExpireStaleCheckoutsCommand cmd, CancellationToken ct)
    {
        var settled = 0;
        foreach (var reference in await _reservations.FindExpiredAsync(cmd.Max, ct))
        {
            try
            {
                if (await SettleAsync(reference, ct)) settled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not settle expired reservation {Reference}; retrying on the next sweep", reference);
            }
        }
        return settled;
    }

    private async Task<bool> SettleAsync(string reference, CancellationToken ct)
    {
        var order = OrderStockReference.TryParse(reference, out var orderId)
            ? await _orders.GetWithItemsAsync(orderId, ct)
            : null;

        switch (order?.Status)
        {
            case null or OrderStatus.Cancelled:
                await _reservations.CancelAsync(reference, ExpiredNote, expired: true, ct);
                return true;
            case OrderStatus.Pending:
                break;
            default:
                await _reservations.CommitAsync(reference, ct);
                return true;
        }

        if (order.PaymentIntentId is { } intentId)
        {
            var state = await _payment.CancelIntentAsync(intentId, ct);
            if (state == PaymentIntentState.Processing) return false;
            if (state == PaymentIntentState.Succeeded)
            {
                await _confirmation.ConfirmAsync(order, ct);
                return true;
            }
        }

        await _confirmation.CancelAsync(order, ExpiredNote, expired: true, ct);
        _logger.LogInformation("Order {OrderId} expired unpaid; its reservation was released", order.Id);
        return true;
    }
}
