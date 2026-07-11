using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Commands;

// الأمر يحمل ما يحتاجه إنشاء الطلب: العميل، العنوان، الأسطر المطلوبة، وكوبون
// اختياري. لا رمز دفع هنا — Stripe.js يتولّى تفاصيل البطاقة مباشرة من المتصفّح؛
// هذا الأمر ينشئ الطلب Pending فقط ويعيد نيّة دفع (ClientSecret) للواجهة.
public record CreateOrderCommand(
    int CustomerId,
    string ShippingAddress,
    List<OrderLineInput> Items,
    string? CouponCode
) : IRequest<Result<OrderCreatedDto>>;

public record OrderLineInput(int ProductId, int Quantity);

public record OrderCreatedDto(
    int OrderId, string Status,
    decimal Subtotal, decimal? DiscountAmount, decimal TotalAmount, string Currency,
    string ClientSecret);
