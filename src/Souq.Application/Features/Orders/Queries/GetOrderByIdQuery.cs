using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;

namespace Souq.Application.Features.Orders.Queries;

public record OrderItemDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
public record OrderDto(int Id, int CustomerId, string Status, string ShippingAddress,
    decimal Subtotal, decimal? DiscountAmount, string? CouponCode,
    decimal TotalAmount, string Currency, DateTime CreatedAt, List<OrderItemDto> Items,
    string? TrackingNumber, string? ShippingCarrier);

public record GetOrderByIdQuery(int Id) : IRequest<Result<OrderDto>>;

// يراه صاحبه أو من يملك صلاحية إدارة الطلبات؛ غيرهما يُعامَل كأن الطلب غير موجود (404 لا
// 403). الفحص هنا لا في الـ Controller — Phase 0 B7.
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
        return order is null || !_currentUser.CanAccessOwnedBy(order.CustomerId, Permissions.Orders.Manage)
            ? Result<OrderDto>.Failure(Error.NotFound("الطلب غير موجود"))
            : Result<OrderDto>.Success(order);
    }
}
