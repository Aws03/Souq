using MediatR;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Queries;

// طلبات العميل الحالي (شاشة "طلباتي") — الأحدث أولاً. يعيد نفس OrderSummaryDto الذي
// تستخدمه شاشة طلبات المدير — عقد واحد لملخّص الطلب. لا معرّف عميل في الاستعلام: الهوية
// من ICurrentUser حصراً (التوكن)، فلا مسار ولا جسم يمكن التلاعب به (Phase 0 B7).
public record GetMyOrdersQuery : IRequest<IReadOnlyList<OrderSummaryDto>>;

public class GetMyOrdersHandler : IRequestHandler<GetMyOrdersQuery, IReadOnlyList<OrderSummaryDto>>
{
    private readonly IOrderRepository _orders;
    private readonly ICurrentUser _currentUser;

    public GetMyOrdersHandler(IOrderRepository orders, ICurrentUser currentUser)
    {
        _orders = orders; _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> Handle(GetMyOrdersQuery q, CancellationToken ct)
    {
        var orders = await _orders.GetByCustomerAsync(_currentUser.RequireUserId(), ct);
        return orders.Select(o => new OrderSummaryDto(
            o.Id, o.CustomerId, o.Status.ToString(),
            o.TotalAmount.Amount, o.TotalAmount.Currency,
            o.CreatedAt, o.Items.Count)).ToList();
    }
}
