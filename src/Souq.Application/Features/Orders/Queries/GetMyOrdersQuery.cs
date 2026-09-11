using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;

namespace Souq.Application.Features.Orders.Queries;

// طلبات العميل الحالي (شاشة "طلباتي") — مرقّمة، الأحدث أولاً (كانت تعيد كل طلبات العميل
// بأسطرها دفعة واحدة). لا معرّف عميل في الاستعلام: الهوية من ICurrentUser حصراً (B7).
public record GetMyOrdersQuery(int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<OrderSummaryDto>>, IPagedQuery;

public class GetMyOrdersHandler : IRequestHandler<GetMyOrdersQuery, PaginatedList<OrderSummaryDto>>
{
    private readonly IOrderQueries _orders;
    private readonly ICurrentUser _currentUser;

    public GetMyOrdersHandler(IOrderQueries orders, ICurrentUser currentUser)
    {
        _orders = orders; _currentUser = currentUser;
    }

    public Task<PaginatedList<OrderSummaryDto>> Handle(GetMyOrdersQuery q, CancellationToken ct) =>
        _orders.ListForCustomerAsync(_currentUser.RequireCustomerId(), PageRequest.From(q), ct);
}
