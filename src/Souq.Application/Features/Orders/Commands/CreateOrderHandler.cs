using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Models;
using Souq.Application.Features.Analytics.Contracts;
using Souq.Application.Features.Orders.Checkout;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Orders.Commands;

// ============================================================================
// CreateOrderHandler — "منسّق" حالة الاستخدام الأهم في النظام، وثلاث مراحل لا أكثر:
//
//   (1) CheckoutQuote    — تحقّق بلا أثر: العميل، العنوانان، الأسطر، التسعير، المتاح، الكوبون، الشحن.
//                          أي رفض هنا يترك النظام كما كان تماماً، برمزه الثابت.
//   (2) OrderPlacement   — معاملة واحدة: الرقم، التثبيت، الحفظ، حجز الكوبون، حجز المخزون. الكلّ أو لا شيء.
//   (3) CheckoutPayment  — نيّة الدفع خارج المعاملة (ADR-0021)، وتعويضها إن فشلت البوّابة.
//
// **لماذا ثلاثة بدل دالّة واحدة (TD-13، قُسِّم في M5):** كانت هذه الدالّة 120 سطراً بستّ عشرة تبعية، والمسار
// الأسهل كسراً فيها — التعويض بعد فشل البوّابة — كتلةَ `catch` في منتصفها. الحدود الثلاثة ليست تجميلاً: كلّ
// منها له **ضمانة مختلفة** (بلا أثر / الكلّ أو لا شيء / خارج المعاملة مع تعويض)، وخلطها في موضع واحد هو ما
// يجعل تغييراً صغيراً يكسر ضمانةً لم ينتبه إليها كاتبه. السلة تُستهلك عند تأكيد الدفع لا هنا.
// ============================================================================
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<OrderCreatedDto>>
{
    private readonly CheckoutQuote _quote;
    private readonly OrderPlacement _placement;
    private readonly CheckoutPayment _payment;
    private readonly IEventSink _events;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        CheckoutQuote quote, OrderPlacement placement, CheckoutPayment payment,
        IEventSink events, ILogger<CreateOrderHandler> logger)
    {
        _quote = quote; _placement = placement; _payment = payment; _events = events; _logger = logger;
    }

    public async Task<Result<OrderCreatedDto>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var draft = await _quote.PrepareAsync(cmd, ct);
        if (!draft.IsSuccess) return Result<OrderCreatedDto>.Failure(draft.Error!);

        var order = await _placement.PlaceAsync(draft.Value!, ct);

        var started = await _payment.StartAsync(order, ct);
        if (!started.IsSuccess) return Result<OrderCreatedDto>.Failure(started.Error!);

        // بدءُ الدفع (C9، ADR-0050 §3): الطلبُ قائمٌ وله وسيلةُ دفع، ولم يُدفع بعد. وهو الطرفُ
        // الآخر للقُمع: ما بينه وبين `order.placed` هو **الهجر**، ولا يُستخرج من الطلبات وحدها.
        if (_events.Enabled)
        {
            try
            {
                _events.Record(BehaviouralEventNames.CheckoutStarted, new CheckoutStartedPayload(
                    order.TotalAmount.Amount, order.TotalAmount.Currency, order.Items.Count));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Behavioural checkout.started event rejected; the order is unaffected");
            }
        }

        return Result<OrderCreatedDto>.Success(new OrderCreatedDto(
            order.Id, order.OrderNumber, order.Status.ToString(),
            order.Subtotal.Amount, order.DiscountAmount?.Amount, order.TotalAmount.Amount, order.TotalAmount.Currency,
            started.Value!, order.ShippingCost.Amount));
    }
}
