using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Orders.Queries;

public record OrderItemDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

// سطر في سجلّ حالة الطلب. الملاحظة ومن غيّرها للإدارة فقط؛ صاحب الطلب يرى الحالة وتاريخها.
public record OrderHistoryEntryDto(string Status, DateTime ChangedAt, string? Note, string? ChangedBy, string? ChangedByName);

// تفاصيل طلب (المرحلة 9): الرقم، اللقطات (الأسطر، العنوانان، الإجماليات)، رمز رابط التتبّع العام لمشاركته، والسجلّ.
// AllowedActions: إجراءات الإدارة المتاحة الآن من جدول الانتقالات (فارغة للعميل). CanCancel: العميل صاحبه يستطيع
// إلغاءه الآن (قبل الدفع).
public record OrderDto(
    int Id, int OrderNumber, int CustomerId, string Status, string ShippingAddress, string BillingAddress,
    decimal Subtotal, decimal? DiscountAmount, string? CouponCode, decimal TotalAmount, string Currency,
    DateTime CreatedAt, List<OrderItemDto> Items, string? TrackingNumber, string? ShippingCarrier, string TrackingToken,
    List<OrderHistoryEntryDto> History, List<string> AllowedActions, bool CanCancel);

public record GetOrderByIdQuery(int Id) : IRequest<Result<OrderDto>>;

// يراه صاحبه أو من يملك صلاحية عرض الطلبات؛ غيرهما يُعامَل كأن الطلب غير موجود (404 لا 403). الفحص هنا لا في
// الـ Controller — Phase 0 B7. والعقد يُشكَّل للناظر: الإدارة ترى الملاحظات ومن غيّر وإجراءاتها، والعميل حالته وإلغاءه.
public class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, Result<OrderDto>>
{
    private readonly IOrderQueries _orders;
    private readonly ICurrentUser _currentUser;

    public GetOrderByIdHandler(IOrderQueries orders, ICurrentUser currentUser)
    {
        _orders = orders; _currentUser = currentUser;
    }

    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery q, CancellationToken ct)
    {
        var order = await _orders.FindAsync(q.Id, ct);
        if (order is null || !_currentUser.CanAccessOwnedBy(order.CustomerId, Permissions.Orders.View))
            return Result<OrderDto>.Failure(Error.NotFound("الطلب غير موجود"));

        var status = Enum.Parse<OrderStatus>(order.Status);
        var isOwner = _currentUser.CustomerId == order.CustomerId;
        var isStaff = _currentUser.HasPermission(Permissions.Orders.View);

        return Result<OrderDto>.Success(order with
        {
            History = isStaff ? order.History : order.History.Select(h => h with { Note = null, ChangedBy = null, ChangedByName = null }).ToList(),
            AllowedActions = _currentUser.HasPermission(Permissions.Orders.Manage)
                ? OrderStatusActions.Available(status).Select(a => a.ToString()).ToList()
                : [],
            CanCancel = isOwner && OrderTransitions.CustomerCanCancel(status),
        });
    }
}
