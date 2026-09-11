using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Baskets.Pricing;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// التحقّق بلا أثر، ثم الطلب والحجز في معاملة واحدة، ثم نيّة الدفع خارجها — وتعويض فشل البوّابة (المرحلة 6).
public class CreateOrderHandlerTests
{
    private const int SavedOrderId = 77;

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IInventoryReservations _reservations = Substitute.For<IInventoryReservations>();
    private readonly IStockAvailability _availability = Substitute.For<IStockAvailability>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IBasketCheckout _baskets = Substitute.For<IBasketCheckout>();
    private readonly ICouponRedemptionRepository _couponUses = Substitute.For<ICouponRedemptionRepository>();
    private readonly Souq.Application.Features.Payments.Contracts.IOrderPayments _orderPayments =
        Substitute.For<Souq.Application.Features.Payments.Contracts.IOrderPayments>();
    private readonly Souq.Application.Features.Coupons.Contracts.ICouponRedemptions _couponRedemptions =
        Substitute.For<Souq.Application.Features.Coupons.Contracts.ICouponRedemptions>();
    private readonly IOrderNumbers _numbers = Substitute.For<IOrderNumbers>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();
    private readonly List<string> _steps = [];
    private Order? _saved;

    public CreateOrderHandlerTests()
    {
        // الحفظ الأول يولّد معرّف الطلب (كما تفعل القاعدة) — الحجز يحمل هذا المعرّف مرجعاً.
        _orders.When(o => o.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())).Do(call => _saved = call.Arg<Order>());
        _uow.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ =>
        {
            if (_saved is { Id: 0 }) TestCatalog.WithId(_saved, SavedOrderId);
            _steps.Add("save");
        });
        _reservations.When(r => r.ReserveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ReservationLine>>(), Arg.Any<CancellationToken>()))
            .Do(_ => _steps.Add("reserve"));
        _payment.When(p => p.CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => _steps.Add("intent"));
        _numbers.NextAsync(Arg.Any<CancellationToken>()).Returns(1001);
    }

    // التسعير الحقيقي (المرحلة 8) فوق مستودعات بديلة — الأسعار والخصم كما في السلة تماماً.
    private CreateOrderHandler CreateHandler() => new(
        _orders, _customers, new PricingService(_products, _coupons, _couponUses, TestTenant.Context(), new FixedClock()), _baskets,
        _numbers, _couponRedemptions, _orderPayments, _reservations, _availability, _payment,
        new OrderPaymentConfirmation(_orders, _reservations, _customers, _couponRedemptions, _orderPayments, _baskets, _payment,
            Substitute.For<IEmailService>(), _uow),
        TestCurrentUser.Customer(1), TestTenant.Context(), _uow, new FixedClock(), NullLogger<CreateOrderHandler>.Instance);

    private static Customer NewCustomer() => new(userId: 1, "عميل", "customer@souq.com");

    // المعرّف 1 (المنتج ومتغيّره الافتراضي) يطابق ProductId في الأمر كما بعد الحفظ فعلياً.
    private static Product NewProduct() => TestCatalog.Product("سماعات لاسلكية", price: 50, id: 1);

    // العميل يأتي من ICurrentUser (العميل 1 في CreateHandler) — الأمر لا يحمل معرّفه.
    private static CreateOrderCommand NewCommand(int quantity = 1, string? couponCode = null) => new(
        ShippingAddress: "عمّان",
        Items: new List<OrderLineInput> { new(ProductId: 1, quantity) },
        CouponCode: couponCode);

    private void Arrange(Product? product = null, int available = 10)
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewCustomer());
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Product> { product ?? NewProduct() });
        _availability.AvailableAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, int> { [1] = available });
        _payment.CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentIntentResult("pi_123", "pi_123_secret"));
    }

    [Fact]
    public async Task عميل_غير_موجود_يُفشل_مبكراً_بلا_أي_نيّة_دفع()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var result = await CreateHandler().Handle(NewCommand(), CancellationToken.None);

        result.ErrorCode.Should().Be("CustomerNotFound");
        await _payment.DidNotReceive().CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_غير_موجود_أو_غير_منشور_لا_يُطلب()
    {
        var draft = NewProduct();
        draft.ChangeStatus(ProductStatus.Draft);
        Arrange(draft);

        var result = await CreateHandler().Handle(NewCommand(), CancellationToken.None);

        result.ErrorCode.Should().Be("ProductNotFound");
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        _steps.Should().BeEmpty();
    }

    [Fact]
    public async Task متاح_غير_كافٍ_يُرفض_قبل_أي_طلب_أو_حجز()
    {
        Arrange(available: 1);

        var result = await CreateHandler().Handle(NewCommand(quantity: 2), CancellationToken.None);

        result.ErrorCode.Should().Be("InsufficientStock");
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        _steps.Should().BeEmpty();
    }

    [Fact]
    public async Task كوبون_غير_موجود_يُفشل_بلا_طلب_ولا_حجز()
    {
        Arrange();
        _coupons.GetByCodeAsync("BAD", Arg.Any<CancellationToken>()).Returns((Coupon?)null);

        var result = await CreateHandler().Handle(NewCommand(couponCode: "BAD"), CancellationToken.None);

        result.ErrorCode.Should().Be("CouponNotFound");
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        _steps.Should().BeEmpty();
    }

    [Fact]
    public async Task نجاح_الإنشاء_يحفظ_الطلب_ثم_يحجز_بمرجعه_في_معاملة_ثم_ينشئ_نيّة_الدفع()
    {
        Arrange();

        var result = await CreateHandler().Handle(NewCommand(quantity: 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be(SavedOrderId);
        result.Value.Status.Should().Be(nameof(OrderStatus.Pending));
        result.Value.TotalAmount.Should().Be(100); // 50 × 2
        result.Value.ClientSecret.Should().Be("pi_123_secret");
        _saved!.PaymentIntentId.Should().Be("pi_123");

        // الطلب يُحفظ (معرّفه) ثم يُحجز مخزونه في المعاملة نفسها، ونيّة الدفع بعدها خارجها، ثم حفظ ربطها.
        _steps.Should().Equal("save", "reserve", "intent", "save");
        await _uow.Received(1).InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await _reservations.Received(1).ReserveAsync(OrderStockReference.For(SavedOrderId),
            Arg.Is<IReadOnlyList<ReservationLine>>(lines => lines.Count == 1 && lines[0].VariantId == 1 && lines[0].Quantity == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task كوبون_صالح_يُطبَّق_على_الإجمالي_قبل_إنشاء_نيّة_الدفع()
    {
        Arrange();
        var coupon = new Coupon("SAVE10", DiscountType.Percentage, 10, null, null, null);
        _coupons.GetByCodeAsync("SAVE10", Arg.Any<CancellationToken>()).Returns(coupon);

        var result = await CreateHandler().Handle(NewCommand(quantity: 2, couponCode: "SAVE10"), CancellationToken.None);

        result.Value!.Subtotal.Should().Be(100);
        result.Value.DiscountAmount.Should().Be(10);
        result.Value.TotalAmount.Should().Be(90);
        await _payment.Received(1).CreateIntentAsync(
            Arg.Is<Money>(m => m.Amount == 90), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // استخدام الكوبون يُحجز في معاملة الطلب نفسها (المرحلة 10) — بقواعده على قراءة جديدة.
        await _couponRedemptions.Received(1).ReserveAsync("SAVE10", SavedOrderId, 1,
            Arg.Is<Money>(m => m.Amount == 100), Arg.Is<Money>(m => m.Amount == 10), Arg.Any<CancellationToken>());
        // دفعة الطلب تُسجَّل بمبلغ النيّة نفسه والحساب الذي أنشأها (المرحلة 11).
        await _orderPayments.Received(1).RecordIntentAsync(SavedOrderId, Arg.Any<PaymentIntentResult>(),
            Arg.Is<Money>(m => m.Amount == 90), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الطلب_يأخذ_رقم_المتجر_ويُثبَّت_داخل_معاملة_الحجز()
    {
        Arrange();

        var result = await CreateHandler().Handle(NewCommand(quantity: 2), CancellationToken.None);

        result.Value!.OrderNumber.Should().Be(1001);
        (_saved!.OrderNumber, _saved.IsPlaced, _saved.PlacedTotal).Should().Be((1001, true, 100m));
        _saved.BillingAddress.Should().Be(_saved.ShippingAddress, "بلا عنوان فوترة في الدفتر ⇒ عنوان الشحن");
        await _numbers.Received(1).NextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بلا_أسطر_يُنشأ_الطلب_من_سلة_العميل_والسلة_الفارغة_تُرفض()
    {
        Arrange();
        _baskets.LinesForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(new List<PricingLine> { new(1, 3) });

        var fromBasket = await CreateHandler().Handle(NewCommand() with { Items = null }, CancellationToken.None);

        fromBasket.Value!.TotalAmount.Should().Be(150);
        _saved!.Items.Single().Quantity.Should().Be(3);

        _baskets.LinesForCustomerAsync(1, Arg.Any<CancellationToken>()).Returns(new List<PricingLine>());
        (await CreateHandler().Handle(NewCommand() with { Items = [] }, CancellationToken.None))
            .ErrorCode.Should().Be("BasketEmpty");
    }

    [Fact]
    public async Task نفاد_بين_القراءة_والحجز_يرفض_ولا_تُنشأ_نيّة_دفع()
    {
        // سباق: المتاح كان كافياً عند القراءة، ثم سبق مشترٍ آخر — الحجز يرفض ويُلغي المعاملة كلها.
        Arrange();
        _reservations.ReserveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ReservationLine>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InsufficientStockException("سماعات", 1, 0));

        var act = () => CreateHandler().Handle(NewCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientStockException>();
        await _payment.DidNotReceive().CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task فشل_بوّابة_الدفع_يُلغي_الطلب_ويحرّر_حجزه_فوراً()
    {
        // Phase 0 C6: كان الطلب يبقى Pending يحجز المخزون للأبد بلا أي وسيلة دفع.
        Arrange();
        _payment.CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("gateway down"));

        var result = await CreateHandler().Handle(NewCommand(quantity: 3), CancellationToken.None);

        result.ErrorCode.Should().Be("PaymentUnavailable");
        _saved!.Status.Should().Be(OrderStatus.Cancelled);
        await _reservations.Received(1).CancelAsync(
            OrderStockReference.For(SavedOrderId), "تعذّر بدء عملية الدفع", false, Arg.Any<CancellationToken>());
        // حفظ الطلب مع الحجز، ثم حفظ الإلغاء مع التحرير — كلٌّ في معاملته.
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _uow.Received(2).InTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }
}
