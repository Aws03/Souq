using MediatR;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
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
    private const string PaymentFailedNote = "فشل الدفع";

    private readonly IOrderRepository _orders;
    private readonly OrderStockRelease _stockRelease;
    private readonly ICustomerRepository _customers;
    private readonly ICouponRepository _coupons;
    private readonly IPaymentService _payment;
    private readonly IEmailService _email;
    private readonly IUnitOfWork _uow;

    public ConfirmOrderPaymentHandler(
        IOrderRepository orders, OrderStockRelease stockRelease, ICustomerRepository customers,
        ICouponRepository coupons, IPaymentService payment, IEmailService email, IUnitOfWork uow)
    {
        _orders = orders; _stockRelease = stockRelease; _customers = customers;
        _coupons = coupons; _payment = payment; _email = email; _uow = uow;
    }

    public async Task<Result<OrderConfirmedDto>> Handle(ConfirmOrderPaymentCommand cmd, CancellationToken ct)
    {
        var order = await _orders.GetWithItemsAsync(cmd.OrderId, ct);
        if (order is null)
            return Result<OrderConfirmedDto>.Failure(Error.NotFound("الطلب غير موجود"));

        // مضمونة التكرار (Idempotent): وصول تأكيدين لنفس الطلب (من العميل ومن
        // الـ Webhook معاً، أو تكرار Webhook) لا يجب أن يُطبّق أثراً مرتين.
        if (order.Status != OrderStatus.Pending)
            return Result<OrderConfirmedDto>.Success(ToDto(order, order.Status));

        if (string.IsNullOrEmpty(order.PaymentIntentId))
            return Result<OrderConfirmedDto>.Failure(
                Error.BusinessRule("NoPaymentIntent", "لا توجد نيّة دفع مرتبطة بهذا الطلب"));

        var confirmation = await _payment.ConfirmAsync(order.PaymentIntentId, ct);
        if (!confirmation.Succeeded)
        {
            // تعويض فشل الدفع: نُلغي الطلب ونعيد مخزونه مع أثر في سجلّ الحركة.
            var reason = confirmation.FailureReason ?? PaymentFailedNote;
            order.Cancel(reason);
            await _stockRelease.ReleaseAsync(order, reason, ct);
            await _uow.SaveChangesAsync(ct);

            return Result<OrderConfirmedDto>.Failure(Error.BusinessRule("PaymentFailed", reason));
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

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // سباق مع مسار التأكيد الآخر (العميل ضد الـ Webhook): الفائز حفظ أولاً و
            // rowversion رفض نسختنا القديمة. نقرأ الحالة الحقيقية دون تتبّع — إن كان
            // الطلب قد دُفع فهذا نجاح مضمون التكرار، بلا كوبون مكرّر ولا بريد ثانٍ.
            var current = await _orders.GetStatusAsync(order.Id, ct);
            if (current is null or OrderStatus.Pending) throw;
            return Result<OrderConfirmedDto>.Success(ToDto(order, current.Value));
        }

        // أثر جانبي غير حرج (البريد): لو فشل لا نُفشل تأكيد الطلب.
        var customer = await _customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is not null)
            await _email.SendOrderConfirmationAsync(customer.Email, order.Id, ct);

        return Result<OrderConfirmedDto>.Success(ToDto(order, order.Status));
    }

    private static OrderConfirmedDto ToDto(Order order, OrderStatus status) =>
        new(order.Id, status.ToString(), order.TotalAmount.Amount, order.TotalAmount.Currency);
}
