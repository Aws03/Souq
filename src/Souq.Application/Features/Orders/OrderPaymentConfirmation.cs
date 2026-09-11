using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Coupons.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders;

public record OrderConfirmedDto(int OrderId, string Status, decimal TotalAmount, string Currency);

// ============================================================================
// OrderPaymentConfirmation — منطق تأكيد الدفع الواحد لثلاثة مداخل بتفويض مختلف:
//   العميل (ConfirmOrderPaymentCommand): مُصادَق بالتوكن + يجب أن يملك الطلب.
//   البوّابة (ProcessPaymentWebhookCommand): مُصادَقة بالتوقيع، لا مستخدم خلفها.
//   منسّق انتهاء المهلة وإلغاء العميل: البوّابة قالت إن الدفع نجح قبل الإلغاء.
// المستدعي يحمّل الطلب ويقرّر الوصول؛ هذا الصنف لا يعرف من المستدعي (B7). الدفع في كل المداخل حكمُ البوّابة، فيُسجَّل
// باسمها (المرحلة 9).
//
// يحقّق من الحالة لدى البوّابة نفسها، مضمون التكرار، ويحسم سباق العميل/الـ Webhook عبر rowversion (ADR-0013).
// المخزون (المرحلة 6): نجاح الدفع يُلتزم الحجز (هنا وحده يُسجَّل البيع)، والفشل يحرّره — كلاهما في معاملة الطلب.
// السلة (المرحلة 9): ما دُفع ثمنه يُستهلك من سلة العميل في المعاملة نفسها.
// ============================================================================
public sealed class OrderPaymentConfirmation
{
    private const string PaymentFailedNote = "فشل الدفع";

    private readonly IOrderRepository _orders;
    private readonly IInventoryReservations _reservations;
    private readonly ICustomerRepository _customers;
    private readonly ICouponRedemptions _couponRedemptions;
    private readonly IBasketCheckout _baskets;
    private readonly IPaymentService _payment;
    private readonly IEmailService _email;
    private readonly IUnitOfWork _uow;

    public OrderPaymentConfirmation(
        IOrderRepository orders, IInventoryReservations reservations, ICustomerRepository customers,
        ICouponRedemptions couponRedemptions, IBasketCheckout baskets, IPaymentService payment, IEmailService email, IUnitOfWork uow)
    {
        _orders = orders; _reservations = reservations; _customers = customers;
        _couponRedemptions = couponRedemptions; _baskets = baskets; _payment = payment; _email = email; _uow = uow;
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

        // استدعاء خارجي خارج أي معاملة قاعدة بيانات مفتوحة (ADR-0021).
        var confirmation = await _payment.ConfirmAsync(order.PaymentIntentId, ct);
        if (!confirmation.Succeeded)
        {
            var reason = confirmation.FailureReason ?? PaymentFailedNote;
            await CancelAsync(order, reason, expired: false, OrderActor.PaymentGateway, ct);
            return Result<OrderConfirmedDto>.Failure(Error.BusinessRule("PaymentFailed", reason));
        }

        order.MarkAsPaid(by: OrderActor.PaymentGateway);

        // استخدام الكوبون حُجز عند إنشاء الطلب (المرحلة 10)؛ الدفع يؤكّده في المعاملة نفسها.
        await _couponRedemptions.ConfirmAsync(order.Id, ct);

        await _baskets.ConsumeAsync(order.CustomerId, order.Items.Select(i => new PricingLine(i.ProductId, i.Quantity)).ToList(), ct);

        try
        {
            await _uow.InTransactionAsync(async () =>
            {
                await _uow.SaveChangesAsync(ct);
                await _reservations.CommitAsync(OrderStockReference.For(order.Id), ct);
            }, ct);
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

    // إلغاء طلب لم يُشحن مع حجوزاته في معاملة واحدة — مسار واحد لفشل الدفع، فشل بدء الدفع، انتهاء المهلة، وإلغاء العميل.
    // استخدام الكوبون يعود للكوبون معه (المرحلة 10).
    public async Task CancelAsync(Order order, string reason, bool expired, OrderActor by, CancellationToken ct)
    {
        order.Cancel(reason, by);
        await _uow.InTransactionAsync(async () =>
        {
            await _uow.SaveChangesAsync(ct);
            await _reservations.CancelAsync(OrderStockReference.For(order.Id), reason, expired, ct);
            await _couponRedemptions.ReleaseAsync(order.Id, ct);
        }, ct);
    }

    private static OrderConfirmedDto ToDto(Order order, OrderStatus status) =>
        new(order.Id, status.ToString(), order.TotalAmount.Amount, order.TotalAmount.Currency);
}
