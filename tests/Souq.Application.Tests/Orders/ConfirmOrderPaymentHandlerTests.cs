using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Events;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

using Souq.Application.Features.Analytics.Contracts;
using Souq.Application.Tests.Analytics;

namespace Souq.Application.Tests.Orders;

// الخطوة الثانية من الدفع: التحقّق من النتيجة لدى البوّابة نفسها، ثم الالتزام بالحجز (نجاح) أو تحريره مع الإلغاء
// (فشل) في معاملة الطلب — بضمان عدم التكرار حتى تحت السباق. الملكية تُفحص في حالة الاستخدام (Phase 0 B7).
public class ConfirmOrderPaymentHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInventoryReservations _reservations = Substitute.For<IInventoryReservations>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IPaymentService _payment = PaymentServiceFake.Create();
    private readonly Souq.Application.Features.Baskets.Contracts.IBasketCheckout _baskets =
        Substitute.For<Souq.Application.Features.Baskets.Contracts.IBasketCheckout>();
    private readonly Souq.Application.Features.Coupons.Contracts.ICouponRedemptions _couponRedemptions =
        Substitute.For<Souq.Application.Features.Coupons.Contracts.ICouponRedemptions>();
    private readonly Souq.Application.Features.Payments.Contracts.IOrderPayments _orderPayments =
        Substitute.For<Souq.Application.Features.Payments.Contracts.IOrderPayments>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();
    private RecordingEventSink _events = new();

    // الطلبات في هذه الاختبارات يملكها العميل 1 (انظر PendingOrderWithIntent).
    private ConfirmOrderPaymentHandler CreateHandler(ICurrentUser? user = null) => new(
        _orders,
        new OrderPaymentConfirmation(_orders, _reservations, _couponRedemptions, _orderPayments, _baskets, _payment, _uow,
            NullLogger<OrderPaymentConfirmation>.Instance, _events),
        user ?? TestCurrentUser.Customer(1));

    private static Order PendingOrderWithIntent(string paymentIntentId = "pi_123", int quantity = 2)
    {
        var order = TestCatalog.WithId(new Order(1, "عمّان", "JOD"), 9);
        order.AddItem(productId: 1, variantId: 1, "سماعات", new Money(50, "JOD"), quantity);
        order.SetPaymentIntent(paymentIntentId);
        return order;
    }

    [Fact]
    public async Task طلب_غير_موجود_يُفشل()
    {
        _orders.GetWithItemsAsync(99, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(99), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task عميل_آخر_لا_يؤكّد_ولا_يُلغي_طلباً_لا_يملكه_ويرى_404()
    {
        // بلا هذا الفحص كان يكفي عميلاً آخر استدعاء التأكيد قبل اكتمال دفع صاحب الطلب:
        // البوّابة تقول "لم يكتمل" ⇒ يُلغى الطلب ويُحرَّر مخزونه. الآن لا يُلمس شيء.
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler(TestCurrentUser.Customer(2))
            .Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        order.Status.Should().Be(OrderStatus.Pending);
        await _payment.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task مدير_الطلبات_يستطيع_تأكيد_أي_طلب()
    {
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());

        var result = await CreateHandler(TestCurrentUser.Admin()).Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task طلب_مؤكَّد_مسبقاً_يُعيد_النجاح_بلا_استدعاء_البوّابة_ولا_التزام_ثانٍ()
    {
        var order = PendingOrderWithIntent();
        order.MarkAsPaid();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.Value!.Status.Should().Be(nameof(OrderStatus.Paid));
        await _payment.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نيّة_ألغتها_البوّابة_تُلغي_الطلب_وتحرّر_حجزه_في_معاملة_واحدة_ولا_ترسل_بريداً()
    {
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(PaymentIntentState.Cancelled, "بطاقة مرفوضة"));

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.ErrorCode.Should().Be("PaymentFailed");
        order.Status.Should().Be(OrderStatus.Cancelled);
        await _reservations.Received(1).CancelAsync(OrderStockReference.For(9), "بطاقة مرفوضة", false, Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        // المرحلة 14: لا بريد في مسار الطلب — الإلغاء يرفع حدثه (من البوّابة، طلب غير مدفوع) ومعالجه لا يرسل بريداً له.
        order.PendingDomainEvents().Should().Equal(
            new OrderStatusChanged(9, 1, OrderStatus.Pending, OrderStatus.Cancelled, OrderActorKind.PaymentGateway));
        // فشل الدفع يلغي الطلب فيعود استخدام كوبونه (المرحلة 10)، وتُحسم دفعته فاشلةً لا ملغاة (المرحلة 11).
        await _couponRedemptions.Received(1).ReleaseAsync(order.Id, Arg.Any<CancellationToken>());
        await _orderPayments.Received(1).MarkClosedAsync(order.Id, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نجاح_الدفع_يُعلّم_الطلب_مدفوعاً_ويلتزم_الحجز_ويؤكّد_استخدام_الكوبون_ويرفع_حدث_البريد()
    {
        var order = PendingOrderWithIntent(quantity: 2);
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);
        order.ApplyCoupon(coupon.Code, coupon.CalculateDiscount(order.Subtotal));

        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.Value!.Status.Should().Be(nameof(OrderStatus.Paid));
        result.Value.TotalAmount.Should().Be(90); // 100 - 10% خصم
        // الاستخدام حُجز عند إنشاء الطلب (المرحلة 10)؛ الدفع يؤكّده في معاملته، ولا يأخذ استخداماً ثانياً.
        await _couponRedemptions.Received(1).ConfirmAsync(order.Id, Arg.Any<CancellationToken>());
        await _couponRedemptions.DidNotReceive().ReserveAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<Money>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        // دفعة الطلب تُحسم ناجحةً في معاملة الدفع نفسها (المرحلة 11).
        await _orderPayments.Received(1).MarkSucceededAsync(order.Id, Arg.Any<CancellationToken>());
        await _reservations.Received(1).CommitAsync(OrderStockReference.For(9), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        // المرحلة 14: بريد التأكيد وإشعارات الإدارة ينطلقان من هذا الحدث — يُكتب في صندوق الصادر في الحفظ نفسه (AppDbContext).
        order.PendingDomainEvents().Should().Equal(
            new OrderStatusChanged(9, 1, OrderStatus.Pending, OrderStatus.Paid, OrderActorKind.PaymentGateway));
    }

    // ── R-02: "ليس ناجحاً" ليست حالة واحدة ──────────────────────────────────
    // بطاقة مرفوضة تُعيد نيّة Stripe إلى requires_payment_method: نيّة حيّة يعيد العميل المحاولة عليها بالسرّ نفسه.
    // إلغاء الطلب هنا (السلوك السابق) كان يترك تلك النيّة حيّة فتقبض لطلب ملغى — والدفعة Failed لا يقبلها أي استرداد.
    [Fact]
    public async Task بطاقة_مرفوضة_ونيّتها_حيّة_لا_تُلغي_الطلب_كي_يعيد_العميل_المحاولة()
    {
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(PaymentIntentState.Retryable, "بطاقة مرفوضة"));

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.ErrorCode.Should().Be("PaymentFailed");
        order.Status.Should().Be(OrderStatus.Pending, "النيّة ما زالت تقبل محاولة أخرى — منسّق انتهاء المهلة يتولّاه إن هُجر");
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _couponRedemptions.DidNotReceive().ReleaseAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _orderPayments.DidNotReceive().MarkClosedAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        order.PendingDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task دفع_قيد_المعالجة_لا_يُلغي_الطلب_ولا_يؤكّده()
    {
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(PaymentIntentState.Processing, "processing"));

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.ErrorCode.Should().Be("PaymentProcessing");
        order.Status.Should().Be(OrderStatus.Pending);
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // دفاع في العمق: مهما كان السبب (خطأ بوّابة أثناء الإلغاء، بيانات قديمة)، طلب ملغى نجحت نيّته يعني مالاً خارج النظام.
    // يُسجَّل على الدفعة ناجحاً كي يقبله مسار الاسترداد، ولا يُبتلع بنجاح صامت.
    [Fact]
    public async Task نجاح_متأخّر_على_طلب_ملغى_يُسجَّل_ناجحاً_ليُستردّ_ولا_يُبتلع_بصمت()
    {
        var order = PendingOrderWithIntent();
        order.Cancel("أُلغي");
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());
        _orderPayments.MarkCapturedAfterCloseAsync(9, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.ErrorCode.Should().Be("PaymentCapturedOnCancelledOrder");
        order.Status.Should().Be(OrderStatus.Cancelled, "الطلب لا يعود للحياة — المال يُستردّ");
        await _orderPayments.Received(1).MarkCapturedAfterCloseAsync(9, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task طلب_ملغى_لم_تقبض_نيّته_يبقى_ملغى_بلا_أثر()
    {
        var order = PendingOrderWithIntent();
        order.Cancel("أُلغي");
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(PaymentIntentState.Cancelled, "canceled"));

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.Value!.Status.Should().Be(nameof(OrderStatus.Cancelled));
        await _orderPayments.DidNotReceive().MarkCapturedAfterCloseAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task سباق_تأكيدين_متزامنين_الخاسر_يرى_الطلب_مدفوعاً_فينجح_بلا_التزام_ولا_بريد_ثانٍ()
    {
        // العميل والـ Webhook يؤكّدان معاً: الفائز حفظ أولاً (والتزم الحجز)، وrowversion رفض نسختنا.
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());
        _orders.GetStatusAsync(order.Id, Arg.Any<CancellationToken>()).Returns(OrderStatus.Paid);

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.Value!.Status.Should().Be(nameof(OrderStatus.Paid));
        await _reservations.DidNotReceive().CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        // لا بريد ثانٍ: حفظ الخاسر فشل، وAppDbContext لا يكتب أحداث حفظ فاشل في صندوق الصادر (NotificationTests يثبته على SQL
        // Server بتعارض rowversion حقيقي).
    }

    [Fact]
    public async Task تعارض_والطلب_ما_زال_معلّقاً_يُعاد_رميه_ليصل_409()
    {
        // التعارض لم يأتِ من تأكيد آخر (مثلاً كوبون مشترك، أو مخزون استنفد محاولاته) — لا ندّعي نجاحاً غير حقيقي.
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());
        _reservations.CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());
        _orders.GetStatusAsync(order.Id, Arg.Any<CancellationToken>()).Returns(OrderStatus.Pending);

        var act = () => CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ========================================================================
    // **الشراء يُسجَّل حين يُدفع، لا حين يُنشأ الطلب** (C9، ADR-0050 §3).
    //
    // وهذا ليس موضعاً بل معنى: `order.placed` يُعدّ في المجاميع «شراءً»، وطلبٌ أُنشئ ثمّ هُجر
    // قبل الدفع ليس شراءً. خلطُ الاثنين يجعل كلَّ سلّةٍ مهجورة مبيعاً في لوحة التاجر.
    // ========================================================================
    [Fact]
    public async Task الدفع_الناجح_يُسجّل_الشراء_بأسطره_المجمَّدة()
    {
        _events = new RecordingEventSink { Enabled = true };
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());

        (await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        var recorded = _events.Recorded.Should().ContainSingle().Subject;
        recorded.Name.Should().Be(BehaviouralEventNames.OrderPlaced);
        var payload = recorded.Payload.Should().BeOfType<OrderPlacedPayload>().Subject;
        payload.Total.Should().Be(order.TotalAmount.Amount);
        payload.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task دفع_لم_ينجح_لا_يُسجّل_شراءً()
    {
        _events = new RecordingEventSink { Enabled = true };
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(PaymentIntentState.Cancelled));

        await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        _events.Recorded.Should().BeEmpty("طلبٌ لم يُدفع ليس شراءً");
    }
}
