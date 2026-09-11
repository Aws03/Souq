using AwesomeAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// الخطوة الثانية من الدفع: التحقّق من النتيجة لدى البوّابة نفسها، تعويض الفشل (إعادة
// مخزون بأثر في السجلّ + إلغاء) أو إتمام النجاح — بضمان عدم التكرار حتى تحت السباق.
public class ConfirmOrderPaymentHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IStockMovementRepository _movements = Substitute.For<IStockMovementRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ConfirmOrderPaymentHandler CreateHandler() =>
        new(_orders, new OrderStockRelease(_products, _movements), _customers, _coupons, _payment, _email, _uow);

    private static Order PendingOrderWithIntent(string paymentIntentId = "pi_123", int quantity = 2)
    {
        var order = new Order(1, "عمّان");
        order.AddItem(productId: 1, "سماعات", new Money(50), quantity);
        order.SetPaymentIntent(paymentIntentId);
        return order;
    }

    [Fact]
    public async Task طلب_غير_موجود_يُفشل()
    {
        _orders.GetWithItemsAsync(99, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(99), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task طلب_مؤكَّد_مسبقاً_يُعيد_النجاح_بلا_استدعاء_بوّابة_الدفع_مجدداً()
    {
        var order = PendingOrderWithIntent();
        order.MarkAsPaid();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(OrderStatus.Paid));
        await _payment.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task فشل_الدفع_يُعيد_المخزون_بأثر_في_السجلّ_ويُلغي_الطلب_ولا_يرسل_بريداً()
    {
        var product = new Product("سماعات لاسلكية", "وصف", new Money(50), stockQuantity: 8, "headphones", categoryId: 1);
        var order = PendingOrderWithIntent(quantity: 2);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(false, "بطاقة مرفوضة"));

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("PaymentFailed");
        product.StockQuantity.Should().Be(10); // 8 + 2 أُعيدت
        order.Status.Should().Be(OrderStatus.Cancelled);
        // Phase 0 C3: الإعادة كانت بلا أثر في سجلّ الحركة فلا يطابق السجلّ المخزون.
        await _movements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Cancellation && m.QuantityChange == 2),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _email.DidNotReceive().SendOrderConfirmationAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نجاح_الدفع_يُعلّم_الطلب_مدفوعاً_ويستهلك_الكوبون_ويرسل_بريد_التأكيد()
    {
        var order = PendingOrderWithIntent(quantity: 2);
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);
        order.ApplyCoupon(coupon.Code, coupon.CalculateDiscount(order.Subtotal));
        var customer = new Customer("عميل", "customer@souq.com", "hash");

        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(customer);
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>()).Returns(coupon);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentConfirmationResult(true, null));

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(OrderStatus.Paid));
        result.Value.TotalAmount.Should().Be(90); // 100 - 10% خصم
        order.Status.Should().Be(OrderStatus.Paid);
        coupon.UsedCount.Should().Be(1);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _email.Received(1).SendOrderConfirmationAsync(customer.Email, order.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task سباق_تأكيدين_متزامنين_الخاسر_يرى_الطلب_مدفوعاً_فينجح_بلا_بريد_ثانٍ()
    {
        // العميل والـ Webhook يؤكّدان معاً: الفائز حفظ أولاً، وrowversion رفض نسختنا.
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());
        _orders.GetStatusAsync(order.Id, Arg.Any<CancellationToken>()).Returns(OrderStatus.Paid);

        var result = await CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(OrderStatus.Paid));
        await _email.DidNotReceive().SendOrderConfirmationAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تعارض_حفظ_والطلب_ما_زال_معلّقاً_يُعاد_رميه_ليصل_409()
    {
        // التعارض لم يأتِ من تأكيد آخر (مثلاً كوبون مشترك) — لا ندّعي نجاحاً غير حقيقي.
        var order = PendingOrderWithIntent();
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_123", Arg.Any<CancellationToken>()).Returns(new PaymentConfirmationResult(true, null));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());
        _orders.GetStatusAsync(order.Id, Arg.Any<CancellationToken>()).Returns(OrderStatus.Pending);

        var act = () => CreateHandler().Handle(new ConfirmOrderPaymentCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
