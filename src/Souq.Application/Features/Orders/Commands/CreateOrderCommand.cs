using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Commands;

// الأمر يحمل ما يحتاجه إنشاء الطلب: العميل، العنوان، الأسطر المطلوبة، ورمز الدفع.
public record CreateOrderCommand(
    int CustomerId,
    string ShippingAddress,
    List<OrderLineInput> Items,
    string PaymentToken
) : IRequest<Result<OrderCreatedDto>>;

public record OrderLineInput(int ProductId, int Quantity);
public record OrderCreatedDto(int OrderId, string Status, decimal TotalAmount, string Currency);
