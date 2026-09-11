using AwesomeAssertions;
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

namespace Souq.Application.Tests.Orders;

// الخطوة الثانية من الدفع: التحقّق من النتيجة لدى البوّابة نفسها، ثم الالتزام بالحجز (نجاح) أو تحريره مع الإلغاء
// (فشل) في معاملة الطلب — بضمان عدم التكرار حتى تحت السباق. الملكية تُفحص في حالة الاستخدام (Phase 0 B7).
public class ConfirmOrderPaymentHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInventoryReservations _reservations = Substitute.For<IInventoryReservations>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly Souq.Application.Features.Baskets.Contracts.IBasketCheckout _baskets =
        Substitute.For<Souq.Application.Features.Baskets.Contracts.IBasketCheckout>();
    private readonly Souq.Application.Features.Coupons.Contracts.ICouponRedemptions _couponRedemptions =
        Substitute.For<Souq.Application.Features.Coupons.Contracts.ICouponRedemptions>();
    private readonly Souq.Application.Features.Payments.Contracts.IOrderPayments _orderPayments =
        Substitute.For<Souq.Application.Features.Payments.Contracts.IOrderPayments>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();

    // الطلبات في هذه الاختبارات يملكها العميل 1 (انظر PendingOrderWithIntent).
    private ConfirmOrderPaymentHandler CreateHandler(ICurrentUser? user = null) => new(
        _orders,
        new OrderPaymentConfirmation(_orders, _reservations, _couponRedemptions, _orderPayments, _baskets, _payment, _uow),
        user ?? TestCurrentUser.Customer(1));

    private static Order PendingOrderWithIntent(string paymentIntentId = "pi_123", int quantity = 2)
    {
        var order = TestCatalog.WithId(new Order(1, "عمّان", "JOD"), 9);
        order.AddItem(productId: 1, "سماعات", new Money(50, "JOD"), quantity);
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
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));

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
    public async Task فشل_الدفع_يُلغي_الطلب_ويحرّر_حجزه_في_معاملة_واحدة_ولا_يرسل_بريداً()
    {
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(false, "بطاقة مرفوضة"));

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
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));

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

    [Fact]
    public async Task سباق_تأكيدين_متزامنين_الخاسر_يرى_الطلب_مدفوعاً_فينجح_بلا_التزام_ولا_بريد_ثانٍ()
    {
        // العميل والـ Webhook يؤكّدان معاً: الفائز حفظ أولاً (والتزم الحجز)، وrowversion رفض نسختنا.
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));
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
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));
        _reservations.CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());
        _orders.GetStatusAsync(order.Id, Arg.Any<CancellationToken>()).Returns(OrderStatus.Pending);

        var act = () => CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
