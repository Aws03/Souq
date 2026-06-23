using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Queries;

public record OrderItemDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
public record OrderDto(int Id, string Status, string ShippingAddress,
    decimal TotalAmount, string Currency, DateTime CreatedAt, List<OrderItemDto> Items);

public record GetOrderByIdQuery(int Id) : IRequest<Result<OrderDto>>;

public class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, Result<OrderDto>>
{
    private readonly IOrderRepository _orders;
    public GetOrderByIdHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery q, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(q.Id, ct);
        if (order is null) return Result<OrderDto>.Failure("الطلب غير موجود", "NotFound");

        var dto = new OrderDto(
            order.Id, order.Status.ToString(), order.ShippingAddress,
            order.TotalAmount.Amount, order.TotalAmount.Currency, order.CreatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.ProductId, i.ProductName, i.UnitPrice.Amount, i.Quantity, i.LineTotal.Amount)).ToList());

        return Result<OrderDto>.Success(dto);
    }
}
