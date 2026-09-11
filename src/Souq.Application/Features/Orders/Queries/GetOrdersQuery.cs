using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Orders.Queries;

// ملخّص طلب في القوائم (الإدارة و"طلباتي") — عقد واحد، دون الأسطر (تُجلب عند فتح الطلب). الرقم رقم المتجر المتسلسل
// (المرحلة 9)، والإجمالي كما ثُبّت عند الإنشاء.
public record OrderSummaryDto(
    int Id, int OrderNumber, int CustomerId, string? CustomerName, string Status, decimal TotalAmount, string Currency,
    DateTime CreatedAt, int ItemCount);

// طلبات المتجر لشاشة الإدارة — مرقّمة، الأحدث أولاً، بمرشّحات الحالة والبحث (رقم الطلب، اسم العميل أو بريده) والتاريخ
// (المرحلة 9). CustomerId لسجلّ طلبات عميل في صفحة تفاصيله (المرحلة 7). عميل متجر آخر لا طلبات له هنا (المرشّح).
public record GetOrdersQuery(
    int Page = 1, int PageSize = 20, int? CustomerId = null, OrderStatus? Status = null, string? Search = null,
    DateTime? From = null, DateTime? To = null)
    : IRequest<PaginatedList<OrderSummaryDto>>, IPagedQuery;

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PaginatedList<OrderSummaryDto>>
{
    private readonly IOrderQueries _orders;
    public GetOrdersHandler(IOrderQueries orders) => _orders = orders;

    public Task<PaginatedList<OrderSummaryDto>> Handle(GetOrdersQuery q, CancellationToken ct) =>
        _orders.ListAsync(new OrderFilter(q.CustomerId, q.Status, q.Search, q.From, q.To), PageRequest.From(q), ct);
}
