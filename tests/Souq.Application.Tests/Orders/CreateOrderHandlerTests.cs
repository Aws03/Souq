using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Baskets.Pricing;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Checkout;
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
    private readonly IPaymentService _payment = PaymentServiceFake.Create();
    private readonly IBasketCheckout _baskets = Substitute.For<IBasketCheckout>();
    private readonly ICouponRedemptionRepository _couponUses = Substitute.For<ICouponRedemptionRepository>();
    private Souq.Application.Features.Shipping.Contracts.IShippingRateProvider _shipping = TestShipping.None();

    // الافتراضي: لا ملفّ ضريبةٍ مختار (حالُ كل متجرٍ قائم). تُستبدَل في اختبار الضريبة أدناه.
    private Souq.Application.Features.Tax.Contracts.ITaxCalculator _tax = TestTax.None();
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
    // المراحل الثلاث تُركَّب بالبدائل نفسها التي كانت تُمرَّر للمعالج مباشرةً قبل تقسيمه (TD-13، M5): ما يُختبَر
    // هنا هو سلوك الدفع من طرف إلى طرف كما يراه العميل، لا حدود التقسيم — فبقيت كل حالات الاختبار كما هي.
    private CreateOrderHandler CreateHandler()
    {
        var pricing = new PricingService(
            _products, _coupons, _couponUses, _shipping, _tax, TestTenant.Context(), new FixedClock());
        var confirmation = new OrderPaymentConfirmation(_orders, _reservations, _couponRedemptions, _orderPayments,
            _baskets, _payment, _uow, NullLogger<OrderPaymentConfirmation>.Instance);
        return new CreateOrderHandler(
            new CheckoutQuote(_customers, pricing, _baskets, _availability, TestCurrentUser.Customer(1)),
            new OrderPlacement(_orders, _numbers, _couponRedemptions, _reservations, TestTenant.Context(), _uow, new FixedClock()),
            new CheckoutPayment(_payment, _orderPayments, confirmation, _uow, NullLogger<CheckoutPayment>.Instance));
    }

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
    public async Task متجر_بطرق_شحن_يلزمه_اختيار_والمختارة_تدخل_الإجمالي_ونيّة_الدفع_ولقطة_الطلب()
    {
        Arrange();
        _shipping = TestShipping.Methods(TestShipping.Option(7, 3.5m));

        var missing = await CreateHandler().Handle(NewCommand(), CancellationToken.None);
        missing.ErrorCode.Should().Be("ShippingMethodRequired");
        _steps.Should().BeEmpty("مشكلة الشحن تُرفض قبل أي كتابة");

        var result = await CreateHandler().Handle(NewCommand() with { ShippingMethodId = 7 }, CancellationToken.None);

        (result.Value!.TotalAmount, result.Value.ShippingCost).Should().Be((53.5m, 3.5m));
        (_saved!.ShippingMethodName, _saved.ShippingAmount, _saved.ShippingCarrier, _saved.ShippingTrackingUrlTemplate, _saved.PlacedTotal)
            .Should().Be(("توصيل", 3.5m, "Aramex", "https://track.example/{number}", 53.5m));
        await _payment.Received(1).CreateIntentAsync(Arg.Is<Money>(m => m.Amount == 53.5m), Arg.Any<string>(), Arg.Any<CancellationToken>());
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

    // ── المتغيّرات (ProductVariants.md، V1) ── المنتج 1 بمتغيّرين: الافتراضي 1 (50) والثاني 12 (60).
    private static Product TwoVariantProduct()
    {
        var product = NewProduct();
        product.SetPricing(new Money(50, "JOD"), null, "HP-BLACK");
        TestCatalog.WithId(TestCatalog.AddVariant(product, new Money(60, "JOD"), sku: "HP-WHITE"), 12);
        return product;
    }

    [Fact]
    public async Task متغيّرا_المنتج_نفسه_سطران_بسعريهما_ولقطة_SKU_وحجز_لكل_متغيّر()
    {
        Arrange(TwoVariantProduct());
        _availability.AvailableAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, int> { [1] = 5, [12] = 5 });

        var result = await CreateHandler().Handle(NewCommand() with
        {
            Items = [new(ProductId: 1, Quantity: 1, VariantId: 1), new(ProductId: 1, Quantity: 2, VariantId: 12), new(1, 1, 1)],
        }, CancellationToken.None);

        result.Value!.Subtotal.Should().Be(220);
        _saved!.Items.Select(i => (i.ProductId, i.VariantId, i.UnitPrice.Amount, i.Quantity, i.Sku, i.VariantLabel))
            .Should().Equal((1, 1, 50m, 2, "HP-BLACK", "S"), (1, 12, 60m, 2, "HP-WHITE", "L"));
        await _reservations.Received(1).ReserveAsync(OrderStockReference.For(SavedOrderId),
            Arg.Is<IReadOnlyList<ReservationLine>>(lines =>
                lines.Where(l => l.VariantId == 1).Sum(l => l.Quantity) == 2 && lines.Where(l => l.VariantId == 12).Sum(l => l.Quantity) == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_بأكثر_من_متغيّر_بلا_تحديد_أو_بمتغيّر_منتج_آخر_لا_يُطلب()
    {
        Arrange(TwoVariantProduct());

        var required = await CreateHandler().Handle(NewCommand(), CancellationToken.None);
        var foreign = await CreateHandler().Handle(NewCommand() with { Items = [new(1, 1, VariantId: 999)] }, CancellationToken.None);

        required.ErrorCode.Should().Be("VariantRequired");
        foreign.ErrorCode.Should().Be("ProductNotFound");
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        _steps.Should().BeEmpty();
    }

    [Fact]
    public async Task متغيّر_معطّل_لا_يُطلب_ولو_سمّاه_الطلب()
    {
        var product = TwoVariantProduct();
        product.DeactivateVariant(12);
        Arrange(product);

        var result = await CreateHandler().Handle(NewCommand() with { Items = [new(1, 1, VariantId: 12)] }, CancellationToken.None);

        result.ErrorCode.Should().Be("ProductNotFound");
        _steps.Should().BeEmpty();
    }

    [Fact]
    public async Task الطلب_من_السلة_يحفظ_متغيّر_كل_سطر_كما_في_السلة()
    {
        Arrange(TwoVariantProduct());
        _availability.AvailableAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, int> { [1] = 5, [12] = 5 });
        _baskets.LinesForCustomerAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<PricingLine> { new(1, 1, 12) });

        var result = await CreateHandler().Handle(NewCommand() with { Items = null }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _saved!.Items.Single().Should().BeEquivalentTo(new { VariantId = 12, Sku = "HP-WHITE", Quantity = 1 });
        result.Value!.TotalAmount.Should().Be(60);
    }

    [Fact]
    public void سطر_الطلب_بمتغيّر_غير_موجب_مرفوض_شكلاً()
    {
        var validator = new CreateOrderValidator();

        validator.Validate(NewCommand() with { Items = [new(1, 1, VariantId: 0)] }).IsValid.Should().BeFalse();
        validator.Validate(NewCommand() with { Items = [new(1, 1, VariantId: 12)] }).IsValid.Should().BeTrue();
        validator.Validate(NewCommand()).IsValid.Should().BeTrue();
    }

    // ========================================================================
    // سقف الكمية على مسار الدفع، لا على السلة وحدها (M15).
    //
    // السلة تحرس `MaxQuantityPerLine` بثلاث طبقات — مُحقِّق ومجال وقيد في القاعدة — لكن الدفع يقبل
    // `items` صريحة و`CheckoutQuote` يُفضّلها على السلة. فحدٌّ أدنى وحده كان يعني أنّ إرسال المصفوفة
    // مباشرةً يتخطّى القاعدة كلّها: لا شيء بعدها إلا المخزون المتاح.
    //
    // وكميّتان ضخمتان لنفس المتغيّر في طلبٍ واحد أسوأ من تخطّي قاعدة: الأسطر لا تُدمَج، ومجموعها
    // يُحسب بحسابٍ مُدقَّق — ففيضٌ، أي 500 وأثرُ استثناءٍ في السجلّ لكل طلب، على نقطة بلا حدّ معدّل.
    // ========================================================================
    [Theory]
    [InlineData(1, true)]
    [InlineData(Basket.MaxQuantityPerLine, true)]
    [InlineData(Basket.MaxQuantityPerLine + 1, false)]
    [InlineData(int.MaxValue, false)]
    public void سطر_الطلب_لا_يتجاوز_سقف_كمية_السلة(int quantity, bool valid)
    {
        var validator = new CreateOrderValidator();

        validator.Validate(NewCommand() with { Items = [new(1, quantity, VariantId: 12)] })
            .IsValid.Should().Be(valid);
    }

    // نفس المتغيّر مرّتين بكميّتين هائلتين: كان مجموعهما يفيض. السقف يمنع السطرين قبل الجمع.
    [Fact]
    public void سطران_ضخمان_لنفس_المتغيّر_يُرفضان_قبل_أن_يفيض_مجموعهما()
    {
        var validator = new CreateOrderValidator();

        validator.Validate(NewCommand() with
        {
            Items = [new(1, 2_000_000_000, VariantId: 12), new(1, 2_000_000_000, VariantId: 12)],
        }).IsValid.Should().BeFalse();
    }
}
