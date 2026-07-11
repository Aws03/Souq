using FluentAssertions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// يغطّي الترتيب المُقوّى من المرحلة 4 (بند AUDIT ٧): الطلب يُحفظ Pending قبل أي
// تحصيل، وفشل التحصيل يُعيد المخزون ويُلغي الطلب بدل تركه بلا مقابل.
public class CreateOrderHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateOrderHandler CreateHandler() =>
        new(_products, _orders, _customers, _payment, _email, _uow);

    private static Customer NewCustomer() => new("عميل", "customer@souq.com", "hash");
    private static Product NewProduct(int stock = 10) =>
        new("سماعات لاسلكية", "وصف", new Money(50), stock, "headphones", categoryId: 1);

    private static CreateOrderCommand NewCommand(int quantity = 1) => new(
        CustomerId: 1,
        ShippingAddress: "عمّان",
        Items: new List<OrderLineInput> { new(ProductId: 1, quantity) },
        PaymentToken: "tok_test");

    [Fact]
    public async Task عميل_غير_موجود_يُفشل_مبكراً_بلا_أي_تحصيل()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var result = await CreateHandler().Handle(NewCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CustomerNotFound");
        await _payment.DidNotReceive().ChargeAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_غير_موجود_يُفشل_بلا_أي_تحصيل()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateHandler().Handle(NewCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ProductNotFound");
        await _payment.DidNotReceive().ChargeAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task مخزون_غير_كافٍ_يُفشل_بلا_حفظ_وبلا_تحصيل()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewProduct(stock: 0));

        var result = await CreateHandler().Handle(NewCommand(quantity: 1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InsufficientStock");
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _payment.DidNotReceive().ChargeAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task فشل_الدفع_يُعيد_المخزون_ويُلغي_الطلب_ولا_يرسل_بريداً()
    {
        var product = NewProduct(stock: 10);
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _payment.ChargeAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentResult(false, null, "بطاقة مرفوضة"));

        Order? savedOrder = null;
        _orders.When(x => x.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()))
            .Do(ci => savedOrder = ci.Arg<Order>());

        var result = await CreateHandler().Handle(NewCommand(quantity: 3), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("PaymentFailed");

        // المخزون يعود لقيمته الأصلية (10) بعد إنقاصه ثم استعادته.
        product.StockQuantity.Should().Be(10);

        savedOrder.Should().NotBeNull();
        savedOrder!.Status.Should().Be(OrderStatus.Cancelled);

        // نجحنا في الحفظ مرتين: مرة Pending قبل التحصيل، ومرة بعد الإلغاء.
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _email.DidNotReceive().SendOrderConfirmationAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نجاح_الدفع_يحفظ_الطلب_Pending_ثم_يعلّمه_مدفوعاً_ويرسل_بريد_التأكيد()
    {
        var product = NewProduct(stock: 10);
        var customer = NewCustomer();
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(customer);
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _payment.ChargeAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentResult(true, "txn_123", null));

        Order? savedOrder = null;
        _orders.When(x => x.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()))
            .Do(ci => savedOrder = ci.Arg<Order>());

        var result = await CreateHandler().Handle(NewCommand(quantity: 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(OrderStatus.Paid));
        result.Value.TotalAmount.Should().Be(100); // 50 × 2

        product.StockQuantity.Should().Be(8);
        savedOrder!.Status.Should().Be(OrderStatus.Paid);

        // Pending أولاً ثم Paid = حفظان.
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _email.Received(1).SendOrderConfirmationAsync(
            customer.Email, savedOrder.Id, Arg.Any<CancellationToken>());
    }
}
