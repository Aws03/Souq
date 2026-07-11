using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Queries;

// قائمة كل الطلبات (لشاشة طلبات المدير) — مرقّمة، الأحدث أولاً. ملخّص لكل طلب
// دون أسطره الكاملة (تُجلب عند فتح الطلب عبر GetOrderById).
public record OrderSummaryDto(
    int Id, int CustomerId, string Status, decimal TotalAmount, string Currency,
    DateTime CreatedAt, int ItemCount);

public record GetOrdersQuery(int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<OrderSummaryDto>>;

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PaginatedList<OrderSummaryDto>>
{
    private readonly IOrderRepository _orders;
    public GetOrdersHandler(IOrderRepository orders) => _orders = orders;

    public async Task<PaginatedList<OrderSummaryDto>> Handle(GetOrdersQuery q, CancellationToken ct)
    {
        var (items, total) = await _orders.GetPagedAsync(q.Page, q.PageSize, ct);

        var dtos = items.Select(o => new OrderSummaryDto(
            o.Id, o.CustomerId, o.Status.ToString(),
            o.TotalAmount.Amount, o.TotalAmount.Currency,
            o.CreatedAt, o.Items.Count)).ToList();

        return new PaginatedList<OrderSummaryDto>(dtos, total, q.Page, q.PageSize);
    }
}
