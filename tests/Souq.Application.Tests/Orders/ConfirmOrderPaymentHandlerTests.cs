using FluentAssertions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// يغطّي الخطوة الثانية من دفع Stripe.js الحقيقي (مرحلة 6): بعد أن يُصادق العميل
// على الدفع من متصفّحه، هذا المعالج يتحقّق من النتيجة لدى بوّابة الدفع نفسها
// (لا يثق بادّعاء العميل)، ويُطبّق تعويض فشل الدفع (إعادة مخزون + إلغاء) أو
// إتمام النجاح (دفع + استهلاك كوبون + بريد تأكيد) — بضمان عدم التكرار (Idempotent).
public class ConfirmOrderPaymentHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ConfirmOrderPaymentHandler CreateHandler() =>
        new(_orders, _products, _customers, _coupons, _payment, _email, _uow);

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
    public async Task فشل_الدفع_يُعيد_المخزون_ويُلغي_الطلب_ولا_يرسل_بريداً()
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
}
