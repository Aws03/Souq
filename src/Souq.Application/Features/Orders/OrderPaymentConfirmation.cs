using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders;

public record OrderConfirmedDto(int OrderId, string Status, decimal TotalAmount, string Currency);

// ============================================================================
// OrderPaymentConfirmation — منطق تأكيد الدفع الواحد لمدخلين بتفويض مختلف:
//   العميل (ConfirmOrderPaymentCommand): مُصادَق بالتوكن + يجب أن يملك الطلب.
//   البوّابة (ProcessPaymentWebhookCommand): مُصادَقة بالتوقيع، لا مستخدم خلفها.
// كان الـ Webhook يمرّر عبر أمر العميل نفسه؛ ففحص الملكية داخله كان سيرفض البوّابة، وتركه
// في الـ Controller كان يسمح لعميل بتأكيد (أو إلغاء بفشل الدفع!) طلب غيره لو نُسي (B7).
//
// يحقّق من الحالة لدى البوّابة نفسها، مضمون التكرار، ويحسم سباق العميل/الـ Webhook عبر
// rowversion (ADR-0013). المستدعي يحمّل الطلب ويقرّر الوصول؛ هذا الصنف لا يعرف من المستدعي.
// ============================================================================
public sealed class OrderPaymentConfirmation
{
    private const string PaymentFailedNote = "فشل الدفع";

    private readonly IOrderRepository _orders;
    private readonly OrderStockRelease _stockRelease;
    private readonly ICustomerRepository _customers;
    private readonly ICouponRepository _coupons;
    private readonly IPaymentService _payment;
    private readonly IEmailService _email;
    private readonly IUnitOfWork _uow;

    public OrderPaymentConfirmation(
        IOrderRepository orders, OrderStockRelease stockRelease, ICustomerRepository customers,
        ICouponRepository coupons, IPaymentService payment, IEmailService email, IUnitOfWork uow)
    {
        _orders = orders; _stockRelease = stockRelease; _customers = customers;
        _coupons = coupons; _payment = payment; _email = email; _uow = uow;
    }

    public async Task<Result<OrderConfirmedDto>> ConfirmAsync(Order order, CancellationToken ct)
    {
        // مضمونة التكرار (Idempotent): وصول تأكيدين لنفس الطلب (من العميل ومن
        // الـ Webhook معاً، أو تكرار Webhook) لا يجب أن يُطبّق أثراً مرتين.
        if (order.Status != OrderStatus.Pending)
            return Result<OrderConfirmedDto>.Success(ToDto(order, order.Status));

        if (string.IsNullOrEmpty(order.PaymentIntentId))
            return Result<OrderConfirmedDto>.Failure(
                Error.BusinessRule("NoPaymentIntent", "لا توجد نيّة دفع مرتبطة بهذا الطلب"));

        // استدعاء خارجي خارج أي معاملة قاعدة بيانات مفتوحة (DatabaseDesign.md §8).
        var confirmation = await _payment.ConfirmAsync(order.PaymentIntentId, ct);
        if (!confirmation.Succeeded)
        {
            // تعويض فشل الدفع: نُلغي الطلب ونعيد مخزونه مع أثر في سجلّ الحركة — معاملة واحدة.
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
            coupon?.IncrementUsage();
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

        // أثر جانبي غير حرج (البريد) بعد الحفظ: فشله لا يُفشل تأكيد الطلب. المرحلة 14 تنقله
        // إلى صندوق صادر (Outbox) كي لا ينتظر الطلب مزوّد البريد ولا يضيع إشعار.
        var customer = await _customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is not null)
            await _email.SendOrderConfirmationAsync(customer.Email, order.Id, ct);

        return Result<OrderConfirmedDto>.Success(ToDto(order, order.Status));
    }

    private static OrderConfirmedDto ToDto(Order order, OrderStatus status) =>
        new(order.Id, status.ToString(), order.TotalAmount.Amount, order.TotalAmount.Currency);
}
