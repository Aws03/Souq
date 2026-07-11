using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Commands;

// يُستدعى بعد أن يُصادق العميل على الدفع من متصفّحه مع Stripe مباشرة (لا نثق
// بادّعاء العميل وحده — نتحقّق من نيّة الدفع لدى Stripe نفسها هنا). نفس هذا
// الأمر يستدعيه أيضاً Webhook الخاص بـ Stripe (دفاع في العمق: العميل قد يغلق
// المتصفّح قبل استدعاء هذه النقطة، فالـ Webhook يضمن التأكيد حتى بلا تفاعل عميل).
public record ConfirmOrderPaymentCommand(int OrderId) : IRequest<Result<OrderConfirmedDto>>;

public record OrderConfirmedDto(int OrderId, string Status, decimal TotalAmount, string Currency);

public class ConfirmOrderPaymentHandler : IRequestHandler<ConfirmOrderPaymentCommand, Result<OrderConfirmedDto>>
{
    private readonly IOrderRepository _orders;
    private readonly IProductRepository _products;
    private readonly ICustomerRepository _customers;
    private readonly ICouponRepository _coupons;
    private readonly IPaymentService _payment;
    private readonly IEmailService _email;
    private readonly IUnitOfWork _uow;

    public ConfirmOrderPaymentHandler(
        IOrderRepository orders, IProductRepository products, ICustomerRepository customers,
        ICouponRepository coupons, IPaymentService payment, IEmailService email, IUnitOfWork uow)
    {
        _orders = orders; _products = products; _customers = customers;
        _coupons = coupons; _payment = payment; _email = email; _uow = uow;
    }

    public async Task<Result<OrderConfirmedDto>> Handle(ConfirmOrderPaymentCommand cmd, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(cmd.OrderId, ct);
        if (order is null)
            return Result<OrderConfirmedDto>.Failure("الطلب غير موجود", "NotFound");

        // مضمونة التكرار (Idempotent): وصول تأكيدين لنفس الطلب (من العميل ومن
        // الـ Webhook معاً، أو تكرار Webhook) لا يجب أن يُطبّق أثراً مرتين.
        if (order.Status != OrderStatus.Pending)
            return Result<OrderConfirmedDto>.Success(new OrderConfirmedDto(
                order.Id, order.Status.ToString(), order.TotalAmount.Amount, order.TotalAmount.Currency));

        if (string.IsNullOrEmpty(order.PaymentIntentId))
            return Result<OrderConfirmedDto>.Failure("لا توجد نيّة دفع مرتبطة بهذا الطلب", "NoPaymentIntent");

        var confirmation = await _payment.ConfirmAsync(order.PaymentIntentId, ct);
        if (!confirmation.Succeeded)
        {
            // تعويض فشل الدفع: نعيد المخزون ونُلغي الطلب (نفس منطق مرحلة 4).
            foreach (var item in order.Items)
            {
                var product = await _products.GetByIdAsync(item.ProductId, ct);
                if (product is null) continue;
                product.IncreaseStock(item.Quantity);
                _products.Update(product);
            }
            order.Cancel();
            await _uow.SaveChangesAsync(ct);

            return Result<OrderConfirmedDto>.Failure(confirmation.FailureReason ?? "فشل الدفع", "PaymentFailed");
        }

        order.MarkAsPaid();

        // استهلاك الكوبون يُحتسب فقط عند نجاح الدفع فعلياً — لا عند مجرّد تطبيقه
        // على طلب قد يفشل دفعه أو يُهجَر.
        if (order.CouponCode is not null)
        {
            var coupon = await _coupons.GetByCodeAsync(order.CouponCode, ct);
            if (coupon is not null)
            {
                coupon.IncrementUsage();
                _coupons.Update(coupon);
            }
        }

        await _uow.SaveChangesAsync(ct);

        // أثر جانبي غير حرج (البريد): لو فشل لا نُفشل تأكيد الطلب.
        var customer = await _customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is not null)
            await _email.SendOrderConfirmationAsync(customer.Email, order.Id, ct);

        return Result<OrderConfirmedDto>.Success(new OrderConfirmedDto(
            order.Id, order.Status.ToString(), order.TotalAmount.Amount, order.TotalAmount.Currency));
    }
}
