using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Features.Payments.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Orders.Checkout;

// ============================================================================
// المرحلة الثالثة من الدفع (TD-13، قُسِّم في M5): **نيّة الدفع، ومسار التعويض إن فشلت**.
//
// النيّة تُنشأ **خارج أي معاملة** (ADR-0021، وقاعدة AGENTS.md §3 رقم 20): نداء شبكة داخل معاملة يُبقي أقفال
// القاعدة مفتوحة بطول ما تستغرقه بوّابة خارجية — وهي مدّة لا نتحكّم بها.
//
// **وهذا الصنف موجود أساساً لأجل السطر الذي يليه:** فشل البوّابة بعد نجاح الحجز يترك طلباً محجوزاً بلا وسيلة
// دفع، فيُعوَّض فوراً — إلغاء الطلب وتحرير حجزه (Phase 0 C6). كان هذا المسار كتلة `catch` في منتصف دالّة من
// 120 سطراً، وهو أكثر ما يسهل كسره (TD-13). هنا صار شيئاً باسمه، ويحرسه اختباره الخاصّ
// (CreateOrderHandlerTests.فشل_بوّابة_الدفع_يُلغي_الطلب_ويحرّر_حجزه_فوراً).
//
// طلب هُجر بعد ذلك (لا فشل بوّابة، بل متسوّق لم يُكمل) يلتقطه منسّق انتهاء المهلة (ExpireStaleCheckouts).
// ============================================================================
public sealed class CheckoutPayment
{
    private const string PaymentStartFailedNote = "تعذّر بدء عملية الدفع";

    private readonly IPaymentService _payment;
    private readonly IOrderPayments _orderPayments;
    private readonly OrderPaymentConfirmation _confirmation;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CheckoutPayment> _logger;

    public CheckoutPayment(
        IPaymentService payment, IOrderPayments orderPayments, OrderPaymentConfirmation confirmation,
        IUnitOfWork uow, ILogger<CheckoutPayment> logger)
    {
        _payment = payment; _orderPayments = orderPayments; _confirmation = confirmation;
        _uow = uow; _logger = logger;
    }

    public async Task<Result<string>> StartAsync(Order order, CancellationToken ct)
    {
        PaymentIntentResult intent;
        try
        {
            intent = await _payment.CreateIntentAsync(order.TotalAmount, order.Id.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "فشل إنشاء نيّة الدفع للطلب {OrderId} — أُلغي الطلب وحُرّر حجزه", order.Id);
            await _confirmation.CancelAsync(order, PaymentStartFailedNote, expired: false, OrderActor.System, ct);
            return Result<string>.Failure(Error.Unavailable(
                "PaymentUnavailable", "تعذّر بدء عملية الدفع حالياً. لم يُحجز أي مخزون، يُرجى المحاولة لاحقاً."));
        }

        order.SetPaymentIntent(intent.PaymentIntentId);
        // دفعة الطلب (المرحلة 11) بالحساب الذي أنشأ النيّة — تُحفظ مع ربطها بالطلب في الحفظ نفسه.
        await _orderPayments.RecordIntentAsync(order.Id, intent, order.TotalAmount, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<string>.Success(intent.ClientSecret);
    }
}
