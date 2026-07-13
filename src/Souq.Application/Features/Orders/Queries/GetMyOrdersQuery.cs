using MediatR;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Queries;

// طلبات عميل واحد (شاشة "طلباتي") — الأحدث أولاً (نفس ترتيب GetByCustomerAsync
// في المستودع). يعيد نفس OrderSummaryDto الذي تستخدمه شاشة طلبات المدير —
// عقد واحد لملخّص الطلب، لا تكرار. الأمر يحمل CustomerId صراحةً كي يفرضه
// الـ Controller من التوكن، لا من جسم/مسار قابل للتلاعب (كما في CreateOrderCommand).
public record GetMyOrdersQuery(int CustomerId) : IRequest<IReadOnlyList<OrderSummaryDto>>;

public class GetMyOrdersHandler : IRequestHandler<GetMyOrdersQuery, IReadOnlyList<OrderSummaryDto>>
{
    private readonly IOrderRepository _orders;
    public GetMyOrdersHandler(IOrderRepository orders) => _orders = orders;

    public async Task<IReadOnlyList<OrderSummaryDto>> Handle(GetMyOrdersQuery q, CancellationToken ct)
    {
        var orders = await _orders.GetByCustomerAsync(q.CustomerId, ct);
        return orders.Select(o => new OrderSummaryDto(
            o.Id, o.CustomerId, o.Status.ToString(),
            o.TotalAmount.Amount, o.TotalAmount.Currency,
            o.CreatedAt, o.Items.Count)).ToList();
    }
}
