using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;

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
    private readonly IOrderRepository _orders;
    private readonly ICurrentUser _currentUser;

    public GetOrderByIdHandler(IOrderRepository orders, ICurrentUser currentUser)
    {
        _orders = orders; _currentUser = currentUser;
    }

    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery q, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(q.Id, ct);
        if (order is null || !_currentUser.CanAccessOwnedBy(order.CustomerId, Permissions.Orders.Manage))
            return Result<OrderDto>.Failure(Error.NotFound("الطلب غير موجود"));

        var dto = new OrderDto(
            order.Id, order.CustomerId, order.Status.ToString(), order.ShippingAddress,
            order.Subtotal.Amount, order.DiscountAmount?.Amount, order.CouponCode,
            order.TotalAmount.Amount, order.TotalAmount.Currency, order.CreatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.ProductId, i.ProductName, i.UnitPrice.Amount, i.Quantity, i.LineTotal.Amount)).ToList(),
            order.TrackingNumber, order.ShippingCarrier);

        return Result<OrderDto>.Success(dto);
    }
}
