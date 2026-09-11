using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Coupons.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders.Commands;

// الإجراءات المسموحة للإدارة على حالة الطلب. لا نمرّر OrderStatus خاماً كي لا يُطلب انتقال تعسّفي (مثل إعادة الطلب
// لـ Pending)؛ نكشف الانتقالات المعنيّة فقط. الدفع ليس إجراء إدارة (تؤكّده البوّابة).
public enum OrderStatusAction { Ship, Deliver, Cancel }

// الإجراءات المتاحة الآن لحالة، من جدول الانتقالات نفسه (OrderTransitions) — شاشة الإدارة تقرؤها ولا تكرّر القاعدة.
public static class OrderStatusActions
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatusAction> ByTarget = new Dictionary<OrderStatus, OrderStatusAction>
    {
        [OrderStatus.Shipped] = OrderStatusAction.Ship,
        [OrderStatus.Delivered] = OrderStatusAction.Deliver,
        [OrderStatus.Cancelled] = OrderStatusAction.Cancel,
    };

    public static IReadOnlyList<OrderStatusAction> Available(OrderStatus status) =>
        OrderTransitions.TargetsFrom(status).Where(ByTarget.ContainsKey).Select(t => ByTarget[t]).ToList();
}

// Note اختياري لكل إجراء (يُسجَّل في سجلّ تاريخ الطلب). TrackingNumber/ShippingCarrier
// ذَواتَي معنى فقط مع Ship — يُتجاهَلان صامتاً لأي إجراء آخر (الكيان نفسه لا
// يقبلهما إلا داخل MarkAsShipped).
public record UpdateOrderStatusCommand(
    int OrderId, OrderStatusAction Action,
    string? Note = null, string? TrackingNumber = null, string? ShippingCarrier = null
) : IRequest<Result>;

// الموظّف الذي نفّذ الإجراء يُسجَّل مع سطر الحالة (المرحلة 9).
public class UpdateOrderStatusHandler : IRequestHandler<UpdateOrderStatusCommand, Result>
{
    private const string AdminCancellationNote = "إلغاء من الإدارة";

    private readonly IOrderRepository _orders;
    private readonly IInventoryReservations _reservations;
    private readonly ICouponRedemptions _couponRedemptions;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public UpdateOrderStatusHandler(
        IOrderRepository orders, IInventoryReservations reservations, ICouponRedemptions couponRedemptions,
        ICurrentUser currentUser, IUnitOfWork uow)
    {
        _orders = orders; _reservations = reservations; _couponRedemptions = couponRedemptions; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result> Handle(UpdateOrderStatusCommand cmd, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(cmd.OrderId, ct);
        if (order is null)
            return Result.Failure(Error.NotFound("الطلب غير موجود"));

        // الكيان يحرس صحّة الانتقال (جدول OrderTransitions). انتقال غير صالح ⇒ InvalidOrderOperationException يرتفع قبل
        // أي تعديل مخزون أو حفظ (422 مركزياً).
        var by = OrderActor.Staff(_currentUser.RequireUserId());
        switch (cmd.Action)
        {
            case OrderStatusAction.Ship: order.MarkAsShipped(cmd.TrackingNumber, cmd.ShippingCarrier, cmd.Note, by); break;
            case OrderStatusAction.Deliver: order.MarkAsDelivered(cmd.Note, by); break;
            case OrderStatusAction.Cancel: order.Cancel(cmd.Note, by); break;
        }

        if (cmd.Action != OrderStatusAction.Cancel)
        {
            await _uow.SaveChangesAsync(ct);
            return Result.Success();
        }

        // الطلب الملغى لم يُشحن: حجز طلب لم يُدفع يُحرَّر، وبيع طلب مدفوع يعود للموجود بحركة إلغاء، واستخدام كوبونه يعود
        // للكوبون (المرحلة 10) — في معاملة الإلغاء.
        await _uow.InTransactionAsync(async () =>
        {
            await _uow.SaveChangesAsync(ct);
            await _reservations.CancelAsync(OrderStockReference.For(order.Id), cmd.Note ?? AdminCancellationNote, expired: false, ct);
            await _couponRedemptions.ReleaseAsync(order.Id, ct);
        }, ct);
        return Result.Success();
    }
}
