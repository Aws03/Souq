using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// التحقّق (بلا أثر جانبي) ثم التنفيذ (إنقاص المخزون + نيّة دفع)، وتعويض فشل البوّابة.
public class CreateOrderHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IStockMovementRepository _stockMovements = Substitute.For<IStockMovementRepository>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateOrderHandler CreateHandler() =>
        new(_products, _orders, _customers, _coupons, _stockMovements, _payment,
            new OrderStockRelease(_products, _stockMovements), TestCurrentUser.Customer(1), TestTenant.Context(), _uow, new FixedClock(),
            NullLogger<CreateOrderHandler>.Instance);

    private static Customer NewCustomer() => new("عميل", "customer@souq.com", "hash");
    // المعرّف 1 يطابق ProductId في الأمر (كما بعد الحفظ فعلياً) — أسطر الطلب تحمل
    // product.Id، وتحرير المخزون يعيد تحميل المنتج بهذا المعرّف.
    private static Product NewProduct(int stock = 10)
    {
        var product = new Product("سماعات لاسلكية", "وصف", new Money(50, "JOD"), stock, "headphones", categoryId: 1);
        typeof(Souq.Domain.Common.Entity).GetProperty("Id")!.SetValue(product, 1);
        return product;
    }

    // العميل يأتي من ICurrentUser (العميل 1 في CreateHandler) — الأمر لا يحمل معرّفه.
    private static CreateOrderCommand NewCommand(int quantity = 1, string? couponCode = null) => new(
        ShippingAddress: "عمّان",
        Items: new List<OrderLineInput> { new(ProductId: 1, quantity) },
        CouponCode: couponCode);

    [Fact]
    public async Task عميل_غير_موجود_يُفشل_مبكراً_بلا_أي_نيّة_دفع()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var result = await CreateHandler().Handle(NewCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CustomerNotFound");
        await _payment.DidNotReceive().CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_غير_موجود_يُفشل_بلا_أي_نيّة_دفع()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateHandler().Handle(NewCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ProductNotFound");
        await _payment.DidNotReceive().CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task مخزون_غير_كافٍ_يُفشل_بلا_حفظ_وبلا_نيّة_دفع()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewProduct(stock: 0));

        var result = await CreateHandler().Handle(NewCommand(quantity: 1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InsufficientStock");
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _payment.DidNotReceive().CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task كوبون_غير_موجود_يُفشل_بلا_إنقاص_مخزون()
    {
        var product = NewProduct(stock: 10);
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _coupons.GetByCodeAsync("BAD", Arg.Any<CancellationToken>()).Returns((Coupon?)null);

        var result = await CreateHandler().Handle(NewCommand(couponCode: "BAD"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CouponNotFound");
        product.StockQuantity.Should().Be(10);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نجاح_الإنشاء_يحفظ_الطلب_Pending_وينشئ_نيّة_دفع_مربوطة_بالطلب()
    {
        var product = NewProduct(stock: 10);
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _payment.CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentIntentResult("pi_123", "pi_123_secret"));

        Order? savedOrder = null;
        _orders.When(x => x.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()))
            .Do(ci => savedOrder = ci.Arg<Order>());

        var result = await CreateHandler().Handle(NewCommand(quantity: 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(OrderStatus.Pending));
        result.Value.TotalAmount.Should().Be(100); // 50 × 2
        result.Value.ClientSecret.Should().Be("pi_123_secret");

        product.StockQuantity.Should().Be(8);
        savedOrder!.Status.Should().Be(OrderStatus.Pending);
        savedOrder.PaymentIntentId.Should().Be("pi_123");

        // كل بيع يُسجَّل حركة مخزون Sale بكمية سالبة تساوي المطلوب.
        await _stockMovements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Sale && m.QuantityChange == -2),
            Arg.Any<CancellationToken>());

        // حفظ الطلب Pending أولاً، ثم حفظ ثانٍ لربط نيّة الدفع.
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task كوبون_صالح_يُطبَّق_على_الإجمالي_قبل_إنشاء_نيّة_الدفع()
    {
        var product = NewProduct(stock: 10);
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>()).Returns(coupon);
        _payment.CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentIntentResult("pi_123", "pi_123_secret"));

        var result = await CreateHandler().Handle(NewCommand(quantity: 2, couponCode: "SAVE10"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Subtotal.Should().Be(100);
        result.Value.DiscountAmount.Should().Be(10);
        result.Value.TotalAmount.Should().Be(90);

        // نيّة الدفع تُنشأ على الإجمالي بعد الخصم لا قبله.
        await _payment.Received(1).CreateIntentAsync(
            Arg.Is<Money>(m => m.Amount == 90), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task فشل_بوّابة_الدفع_عند_إنشاء_النيّة_يُلغي_الطلب_ويحرّر_مخزونه_فوراً()
    {
        // Phase 0 C6: كان الطلب يبقى Pending يحجز المخزون للأبد بلا أي وسيلة دفع.
        var product = NewProduct(stock: 10);
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _payment.CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("gateway down"));

        Order? savedOrder = null;
        _orders.When(x => x.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()))
            .Do(ci => savedOrder = ci.Arg<Order>());

        var result = await CreateHandler().Handle(NewCommand(quantity: 3), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("PaymentUnavailable");
        savedOrder!.Status.Should().Be(OrderStatus.Cancelled);
        product.StockQuantity.Should().Be(10); // 10 − 3 ثم + 3
        await _stockMovements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Cancellation && m.QuantityChange == 3),
            Arg.Any<CancellationToken>());
        // حفظ الطلب، ثم حفظ التعويض (إلغاء + إعادة) في معاملة واحدة.
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
