using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Queries;

// ملخّص طلب في القوائم (الإدارة و"طلباتي") — عقد واحد، دون الأسطر الكاملة (تُجلب عند فتح
// الطلب عبر GetOrderById).
public record OrderSummaryDto(
    int Id, int CustomerId, string Status, decimal TotalAmount, string Currency,
    DateTime CreatedAt, int ItemCount);

// كل الطلبات لشاشة الإدارة — مرقّمة، الأحدث أولاً. CustomerId (المرحلة 7) لسجلّ طلبات عميل في صفحة تفاصيله؛ عميل
// متجر آخر لا طلبات له هنا (المرشّح).
public record GetOrdersQuery(int Page = 1, int PageSize = 20, int? CustomerId = null)
    : IRequest<PaginatedList<OrderSummaryDto>>, IPagedQuery;

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PaginatedList<OrderSummaryDto>>
{
    private readonly IOrderQueries _orders;
    public GetOrdersHandler(IOrderQueries orders) => _orders = orders;

    public Task<PaginatedList<OrderSummaryDto>> Handle(GetOrdersQuery q, CancellationToken ct) =>
        q.CustomerId is int customerId
            ? _orders.ListForCustomerAsync(customerId, PageRequest.From(q), ct)
            : _orders.ListAsync(PageRequest.From(q), ct);
}
