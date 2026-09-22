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
        StartPaymentAttempt attempt;
        try
        {
            // **القدراتُ تُسأل قبل أن يُعرَض الدفع** (ADR-0048 §5). قاعدتان هنا لهما ضحيّةٌ حقيقية:
            // عملةٌ لا يقبلها الحساب المربوط، ومبلغٌ أدقُّ ممّا يقبله. والثانيةُ ليست تنسيقاً: حين
            // يوثّق مزوّدٌ أنّ شبكةَ بطاقاتٍ تشترط مبلغاً ينتهي بصفرٍ في عملةٍ ثلاثية الخانات، فأصغرُ
            // زيادةٍ ممكنة عشرُ وحداتٍ صغرى — وهو قيدٌ يمتدّ إلى أسعار المنتجات نفسها.
            //
            // **ولا قيمةَ لأيّ سوقٍ مكتوبةٌ في المنتج**: المحوّلُ يعلن ما يوثّقه مزوّدُه، والافتراضُ
            // «بلا قيد». فشلُ الدفع هنا يُعامَل كفشل البوّابة تماماً: الطلبُ يُلغى وحجزُه يُحرَّر.
            var capabilities = await _payment.GetCapabilitiesAsync(ct);
            if (!capabilities.Supports(order.TotalAmount.Currency))
            {
                _logger.LogError("حساب الدفع المربوط لا يقبل العملة {Currency} — أُلغي الطلب {OrderId}",
                    order.TotalAmount.Currency, order.Id);
                await _confirmation.CancelAsync(order, PaymentStartFailedNote, expired: false, OrderActor.System, ct);
                return Result<string>.Failure(Error.Unavailable(
                    "PaymentCurrencyNotSupported", "حساب الدفع المربوط لا يقبل عملة هذا المتجر."));
            }

            attempt = await _payment.StartPaymentAsync(order.TotalAmount, order.Id.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "فشل إنشاء نيّة الدفع للطلب {OrderId} — أُلغي الطلب وحُرّر حجزه", order.Id);
            await _confirmation.CancelAsync(order, PaymentStartFailedNote, expired: false, OrderActor.System, ct);
            return Result<string>.Failure(Error.Unavailable(
                "PaymentUnavailable", "تعذّر بدء عملية الدفع حالياً. لم يُحجز أي مخزون، يُرجى المحاولة لاحقاً."));
        }

        // ====================================================================
        // **الشكلُ يُفرَّق هنا، لا في الواجهة** (ADR-0048 §1). اليومَ يُنتج المحوّلان
        // `ClientScript` وحده، والأشكالُ الأخرى مرفوضةٌ صراحةً بدل أن تُعامَل ضمناً كأنّها هو:
        // مزوّدٌ يُعيد توجيهاً ويُسلَّم سرُّه للواجهة يُنتج شاشةَ دفعٍ فارغة، وهو عطبٌ صامت.
        //
        // فالرفضُ هنا ليس عجزاً بل عقد: مَن يُدخل أوّلَ مزوّدٍ يُعيد التوجيه يُضيف فرعَه وشاشته
        // معاً، ويسقط هذا السطر حين يفعل. والطلبُ يُلغى وحجزُه يُحرَّر كأيّ فشلِ بدء.
        // ====================================================================
        if (attempt.Result is not StartPaymentResult.ClientScript script)
        {
            _logger.LogError(
                "المحوّل {Gateway} بدأ الدفع بشكل {Flow} ولا واجهة له بعد — أُلغي الطلب {OrderId}",
                attempt.Gateway, attempt.Result.GetType().Name, order.Id);
            await _confirmation.CancelAsync(order, PaymentStartFailedNote, expired: false, OrderActor.System, ct);
            return Result<string>.Failure(Error.Unavailable(
                "PaymentFlowNotSupported", "طريقة الدفع لدى المزوّد المربوط غير مدعومة في هذه النسخة."));
        }

        order.SetPaymentIntent(script.ProviderReference);
        // دفعة الطلب (المرحلة 11) بالحساب الذي بدأ الدفع، نوعاً وهويّةً (TD-50) — تُحفظ مع ربطها
        // بالطلب في الحفظ نفسه.
        await _orderPayments.RecordIntentAsync(
            order.Id,
            new PaymentIntentResult(script.ProviderReference, script.ClientSecret, attempt.Gateway, attempt.GatewayAccount),
            order.TotalAmount, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<string>.Success(script.ClientSecret);
    }
}
