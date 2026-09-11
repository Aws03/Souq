using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// الإجراءات المسموحة للمدير على حالة الطلب. لا نمرّر OrderStatus خاماً كي لا
// يُطلب انتقال تعسّفي (مثل إعادة الطلب لـ Pending)؛ نكشف الانتقالات المعنيّة فقط.
public enum OrderStatusAction { Ship, Deliver, Cancel }

// Note اختياري لكل إجراء (يُسجَّل في سجلّ تاريخ الطلب). TrackingNumber/ShippingCarrier
// ذَواتَي معنى فقط مع Ship — يُتجاهَلان صامتاً لأي إجراء آخر (الكيان نفسه لا
// يقبلهما إلا داخل MarkAsShipped).
public record UpdateOrderStatusCommand(
    int OrderId, OrderStatusAction Action,
    string? Note = null, string? TrackingNumber = null, string? ShippingCarrier = null
) : IRequest<Result>;

public class UpdateOrderStatusHandler : IRequestHandler<UpdateOrderStatusCommand, Result>
{
    private const string AdminCancellationNote = "إلغاء من الإدارة";

    private readonly IOrderRepository _orders;
    private readonly OrderStockRelease _stockRelease;
    private readonly IUnitOfWork _uow;

    public UpdateOrderStatusHandler(IOrderRepository orders, OrderStockRelease stockRelease, IUnitOfWork uow)
    {
        _orders = orders; _stockRelease = stockRelease; _uow = uow;
    }

    public async Task<Result> Handle(UpdateOrderStatusCommand cmd, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(cmd.OrderId, ct);
        if (order is null)
            return Result.Failure(Error.NotFound("الطلب غير موجود"));

        // الكيان يحرس صحّة الانتقال (لا يُشحن طلب لم يُدفع...). انتقال غير صالح ⇒
        // InvalidOrderOperationException يرتفع قبل أي تعديل مخزون أو حفظ (422 مركزياً).
        switch (cmd.Action)
        {
            case OrderStatusAction.Ship: order.MarkAsShipped(cmd.TrackingNumber, cmd.ShippingCarrier, cmd.Note); break;
            case OrderStatusAction.Deliver: order.MarkAsDelivered(cmd.Note); break;
            case OrderStatusAction.Cancel: order.Cancel(cmd.Note); break;
        }

        // الطلب الملغى لم يُشحن، فمخزونه المحجوز يعود للبيع — في نفس معاملة الإلغاء.
        if (cmd.Action == OrderStatusAction.Cancel)
            await _stockRelease.ReleaseAsync(order, cmd.Note ?? AdminCancellationNote, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
