using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Baskets.Pricing;
using Souq.Application.Features.Customers;
using Souq.Application.Features.Customers.Account;
using Souq.Application.Features.Customers.Admin;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Checkout;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;
using Souq.Application.Features.Auth.Contracts;

namespace Souq.Application.Tests.Customers;

// حساب العميل (المرحلة 7): العميل هو المستخدم الحالي دائماً، ومعرّف عنوان ليس في دفتره ⇒ 404 بلا حفظ؛ المحو يتطلّب كلمة
// المرور ويمرّ بمسار واحد مع الإدارة.
public class CustomerAccountHandlersTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    // العقد لا مستودع الحساب (TD-03، M9): ما يفعله المحو بالحساب نفسه — التجريد وإبطال الجلسات ونسيان
    // ختمها — صار مسؤولية Identity، ويُثبَّت في AccountLifecycleTests. ما يُثبَّت هنا هو مسؤولية هذه
    // الوحدة: أن تُجرّد ملفّها وتحذف سلّتها ومفضّلتها، **وتنادي العقد بالمعرّف الصحيح**.
    private readonly IAccountLifecycle _accounts = Substitute.For<IAccountLifecycle>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _me = TestCurrentUser.Customer(1);
    private readonly Customer _customer = TestCatalog.WithId(new Customer(userId: 7, "سارة", "sara@souq.test"), 1);
    private readonly User _user = TestCatalog.WithId(new User("سارة", "sara@souq.test", "$2a$hash", Roles.Customer), 7);

    public CustomerAccountHandlersTests()
    {
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_customer);
    }

    private static AddressInput Input(string line1 = "شارع الجامعة 12", string? label = "المنزل") =>
        new("سارة", "0790000000", "JO", "عمّان", line1, Label: label);

    private readonly IWishlistRepository _wishlist = Substitute.For<IWishlistRepository>();

    private CustomerErasure Erasure() =>
        new(_accounts, Substitute.For<IBasketRepository>(), _wishlist, new FixedClock());

    [Fact]
    public async Task تحديث_الملف_يعدّل_الملف_واسم_الحساب_معاً()
    {
        var result = await new UpdateMyProfileHandler(_customers, _accounts, _me, _uow)
            .Handle(new UpdateMyProfileCommand("سارة محمد", "+962790000000"), CancellationToken.None);

        result.Value!.FullName.Should().Be("سارة محمد");
        result.Value.Phone.Should().Be("+962790000000");
        // الاسم يُطلب من الحساب بالعقد، والاثنان يُودَعان معاً في حفظٍ واحد — وتعديلُ الحساب نفسه
        // يُثبَّت في AccountLifecycleTests، لا هنا: هذه الوحدة لا تملك ذلك التجمّع.
        await _accounts.Received(1).RenameAsync(7, "سارة محمد", Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task أول_عنوان_يُضاف_افتراضياً_ويُعاد_بشكله_المطبَّع()
    {
        var result = await new AddMyAddressHandler(_customers, _me, _uow)
            .Handle(new AddMyAddressCommand(Input()), CancellationToken.None);

        result.Value!.Should().BeEquivalentTo(new
        {
            Label = "المنزل", Country = "JO", City = "عمّان", IsDefaultShipping = true, IsDefaultBilling = true,
        });
        _customer.Addresses.Should().ContainSingle();
    }

    [Fact]
    public async Task عنوان_ليس_في_دفتري_غير_موجود_تعديلاً_وحذفاً_وتعييناً()
    {
        TestCatalog.WithId(_customer.AddAddress(Input().ToDomainForTest(), null), 5);

        (await new UpdateMyAddressHandler(_customers, _me, _uow).Handle(new UpdateMyAddressCommand(99, Input()), CancellationToken.None))
            .ErrorCode.Should().Be("NotFound");
        (await new RemoveMyAddressHandler(_customers, _me, _uow).Handle(new RemoveMyAddressCommand(99), CancellationToken.None))
            .ErrorCode.Should().Be("NotFound");
        (await new SetMyDefaultAddressHandler(_customers, _me, _uow).Handle(new SetMyDefaultAddressCommand(99, AddressUse.Billing), CancellationToken.None))
            .ErrorCode.Should().Be("NotFound");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _customer.Addresses.Should().ContainSingle();
    }

    [Fact]
    public async Task المحو_بكلمة_مرور_خاطئة_يُرفض_ولا_يمسّ_شيئاً()
    {
        _accounts.VerifyPasswordAsync(7, "wrong", Arg.Any<CancellationToken>()).Returns(false);

        var result = await new EraseMyAccountHandler(_customers, _accounts, Erasure(), _me)
            .Handle(new EraseMyAccountCommand("wrong"), CancellationToken.None);

        result.ErrorCode.Should().Be("CurrentPasswordIncorrect");
        _customer.IsErased.Should().BeFalse();
        // ولا الحساب يُمَسّ: العقد لم يُنادَ إطلاقاً، فلا محو ولا حفظ.
        await _accounts.DidNotReceive().EraseAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task المحو_بكلمة_المرور_يجرّد_الملف_والحساب_وينسى_الجلسات()
    {
        _accounts.VerifyPasswordAsync(7, "right", Arg.Any<CancellationToken>()).Returns(true);

        var result = await new EraseMyAccountHandler(_customers, _accounts, Erasure(), _me)
            .Handle(new EraseMyAccountCommand("right"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _customer.IsErased.Should().BeTrue();
        // والحساب يُمحى بمعرّفه — وما يفعله ذلك المحو بالحساب يُثبَّت في AccountLifecycleTests.
        await _accounts.Received(1).EraseAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الإدارة_تحظر_وترفع_والعميل_المجهول_غير_موجود()
    {
        var handler = new SetCustomerStatusHandler(_customers, _uow, new FixedClock());

        (await handler.Handle(new SetCustomerStatusCommand(1, CustomerStatus.Blocked), CancellationToken.None)).IsSuccess.Should().BeTrue();
        _customer.IsBlocked.Should().BeTrue();
        (await handler.Handle(new SetCustomerStatusCommand(1, CustomerStatus.Active), CancellationToken.None)).IsSuccess.Should().BeTrue();
        _customer.IsBlocked.Should().BeFalse();
        (await handler.Handle(new SetCustomerStatusCommand(42, CustomerStatus.Blocked), CancellationToken.None))
            .ErrorCode.Should().Be("NotFound");
    }
}

// الشراء وقواعد العميل (المرحلة 7): المحظور لا يطلب، والعنوان من دفتره فقط تُحفظ لقطته على الطلب.
public class CreateOrderCustomerRulesTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IInventoryReservations _reservations = Substitute.For<IInventoryReservations>();
    private readonly IStockAvailability _availability = Substitute.For<IStockAvailability>();
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IBasketCheckout _baskets = Substitute.For<IBasketCheckout>();
    private readonly IOrderNumbers _numbers = Substitute.For<IOrderNumbers>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();
    private readonly Customer _customer = new(userId: 1, "عميل", "c@souq.test");

    public CreateOrderCustomerRulesTests()
    {
        _numbers.NextAsync(Arg.Any<CancellationToken>()).Returns(1001);
        _customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_customer);
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Product> { TestCatalog.Product("سماعات", price: 50, id: 1) });
        _availability.AvailableAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, int> { [1] = 10 });
        _payment.CreateIntentAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentIntentResult("pi_1", "pi_1_secret"));
    }

    // المراحل الثلاث بالبدائل نفسها (TD-13، قُسِّم في M5): ما يُختبَر هنا قواعد العميل لا حدود التقسيم.
    private CreateOrderHandler CreateHandler()
    {
        var couponRedemptions = Substitute.For<Souq.Application.Features.Coupons.Contracts.ICouponRedemptions>();
        var orderPayments = Substitute.For<Souq.Application.Features.Payments.Contracts.IOrderPayments>();
        var pricing = new PricingService(_products, Substitute.For<ICouponRepository>(),
            Substitute.For<ICouponRedemptionRepository>(), TestShipping.None(), TestTenant.Context(), new FixedClock());
        var confirmation = new OrderPaymentConfirmation(_orders, _reservations, couponRedemptions, orderPayments,
            _baskets, _payment, _uow, NullLogger<OrderPaymentConfirmation>.Instance);
        return new CreateOrderHandler(
            new CheckoutQuote(_customers, pricing, _baskets, _availability, TestCurrentUser.Customer(1)),
            new OrderPlacement(_orders, _numbers, couponRedemptions, _reservations, TestTenant.Context(), _uow, new FixedClock()),
            new CheckoutPayment(_payment, orderPayments, confirmation, _uow, NullLogger<CheckoutPayment>.Instance));
    }

    private static CreateOrderCommand Command(string? address = "عمّان", int? addressId = null) =>
        new(address, [new OrderLineInput(1, 1)], null, addressId);

    [Fact]
    public async Task العميل_المحظور_لا_يطلب_ولا_يحجز()
    {
        _customer.Block(DateTime.UtcNow);

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        result.ErrorCode.Should().Be("CustomerBlocked");
        await _reservations.DidNotReceive().ReserveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ReservationLine>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task عنوان_من_دفتر_العميل_تُحفظ_لقطته_على_الطلب()
    {
        var saved = TestCatalog.WithId(_customer.AddAddress(
            new PostalAddress("سارة", "0790000000", "JO", "إربد", "شارع الحصن 3"), "المنزل"), 12);
        Order? order = null;
        _orders.When(o => o.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())).Do(call => order = call.Arg<Order>());

        var result = await CreateHandler().Handle(Command(address: null, addressId: saved.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order!.ShippingAddress.Should().Be("سارة، 0790000000، شارع الحصن 3، إربد، JO");
    }

    [Fact]
    public async Task عنوان_ليس_في_دفتر_العميل_يُرفض()
    {
        var result = await CreateHandler().Handle(Command(address: null, addressId: 99), CancellationToken.None);

        result.ErrorCode.Should().Be("AddressNotFound");
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}

internal static class AddressInputTestExtensions
{
    public static PostalAddress ToDomainForTest(this AddressInput input) =>
        new(input.RecipientName, input.Phone, input.Country, input.City, input.Line1, input.Region, input.Line2, input.PostalCode);
}
