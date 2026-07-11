using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// الإجراءات المسموحة للمدير على حالة الطلب. لا نمرّر OrderStatus خاماً كي لا
// يُطلب انتقال تعسّفي (مثل إعادة الطلب لـ Pending)؛ نكشف الانتقالات المعنيّة فقط.
public enum OrderStatusAction { Ship, Deliver, Cancel }

public record UpdateOrderStatusCommand(int OrderId, OrderStatusAction Action) : IRequest<Result>;

public class UpdateOrderStatusHandler : IRequestHandler<UpdateOrderStatusCommand, Result>
{
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _uow;

    public UpdateOrderStatusHandler(IOrderRepository orders, IUnitOfWork uow)
    {
        _orders = orders; _uow = uow;
    }

    public async Task<Result> Handle(UpdateOrderStatusCommand cmd, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(cmd.OrderId, ct);
        if (order is null)
            return Result.Failure("الطلب غير موجود", "NotFound");

        try
        {
            // الكيان يحرس صحّة الانتقال (لا يُشحن طلب لم يُدفع...). خطأ متوقّع ⇒ Result.
            switch (cmd.Action)
            {
                case OrderStatusAction.Ship: order.MarkAsShipped(); break;
                case OrderStatusAction.Deliver: order.MarkAsDelivered(); break;
                case OrderStatusAction.Cancel: order.Cancel(); break;
            }
        }
        catch (InvalidOrderOperationException ex)
        {
            return Result.Failure(ex.Message, "InvalidTransition");
        }

        _orders.Update(order);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
