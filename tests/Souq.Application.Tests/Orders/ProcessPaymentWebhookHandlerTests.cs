using AwesomeAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Orders;
using Souq.Application.Features.Orders.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// الـ Webhook يمرّ عبر منفذ الدفع (التحقّق من التوقيع هناك) ثم لنفس منطق التأكيد الذي يستخدمه العميل — بلا مستخدم خلفه:
// التفويض هو التوقيع لا فحص الملكية. المرحلة 11: الحدث يُطبَّق في متجره — متجر المضيف، أو المتجر المسمّى في بيانات النيّة
// إن وقّعه حساب النشر المشترك؛ وحدث وقّعه حساب متجر لا يُوجَّه لغيره.
public class ProcessPaymentWebhookHandlerTests
{
    private static readonly TenantInfo OtherStore =
        new(99, "other", "متجر آخر", TenantStatus.Active, "JOD", "ar", "Asia/Amman",
            new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal));

    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly ITenantDirectory _directory = Substitute.For<ITenantDirectory>();
    private readonly ITenantScopeRunner _scopes = Substitute.For<ITenantScopeRunner>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IMediator _otherStoreMediator = Substitute.For<IMediator>();
    private readonly ITenantContext _tenant = TestTenant.Context();

    public ProcessPaymentWebhookHandlerTests()
    {
        _mediator.Send(Arg.Any<ApplyPaymentEventCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _otherStoreMediator.Send(Arg.Any<ApplyPaymentEventCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _scopes.RunAsync(Arg.Any<TenantInfo>(), Arg.Any<Func<IMediator, Task<Result>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result>>>()(_otherStoreMediator));
    }

    private ProcessPaymentWebhookHandler CreateHandler() =>
        new(_payment, _tenant, _directory, _scopes, _mediator, NullLogger<ProcessPaymentWebhookHandler>.Instance);

    private void Parses(PaymentWebhookEvent? evt) =>
        _payment.ParseWebhookAsync("{}", "sig", Arg.Any<CancellationToken>()).Returns(evt);

    [Fact]
    public async Task توقيع_غير_صالح_يُرفض_ولا_يُلمس_أي_طلب()
    {
        _payment.ParseWebhookAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidPaymentWebhookException());

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "bad"), CancellationToken.None);

        result.ErrorCode.Should().Be("InvalidSignature");
        await _mediator.DidNotReceiveWithAnyArgs().Send(default(ApplyPaymentEventCommand)!, default);
    }

    [Fact]
    public async Task حدث_لا_يخصّ_دفعة_طلب_يُقرّ_به_بلا_فعل()
    {
        Parses(null);

        var result = await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _mediator.DidNotReceiveWithAnyArgs().Send(default(ApplyPaymentEventCommand)!, default);
    }

    [Theory]
    [InlineData(null)]   // نيّة ما قبل المرحلة 11: بلا متجر في بياناتها ⇒ متجر المضيف
    [InlineData(1)]      // متجر المضيف (TestTenant)
    public async Task حدث_متجر_المضيف_يُطبَّق_هنا(int? tenantId)
    {
        Parses(new PaymentWebhookEvent("42", "pi_42", tenantId));

        (await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await _mediator.Received(1).Send(new ApplyPaymentEventCommand("42"), Arg.Any<CancellationToken>());
        await _scopes.DidNotReceiveWithAnyArgs().RunAsync<IMediator, Result>(default!, default!);
    }

    [Fact]
    public async Task حدث_حساب_النشر_لمتجر_آخر_يُطبَّق_داخل_نطاق_متجره()
    {
        Parses(new PaymentWebhookEvent("42", "pi_42", OtherStore.Id, VerifiedByStoreAccount: false));
        _directory.FindByIdAsync(OtherStore.Id, Arg.Any<CancellationToken>()).Returns(OtherStore);

        (await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await _scopes.Received(1).RunAsync(OtherStore, Arg.Any<Func<IMediator, Task<Result>>>());
        await _otherStoreMediator.Received(1).Send(new ApplyPaymentEventCommand("42"), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceiveWithAnyArgs().Send(default(ApplyPaymentEventCommand)!, default);
    }

    [Fact]
    public async Task حدث_وقّعه_حساب_متجر_ويسمّي_متجراً_آخر_يُتجاهل()
    {
        Parses(new PaymentWebhookEvent("42", "pi_42", OtherStore.Id, VerifiedByStoreAccount: true));
        _directory.FindByIdAsync(OtherStore.Id, Arg.Any<CancellationToken>()).Returns(OtherStore);

        (await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await _scopes.DidNotReceiveWithAnyArgs().RunAsync<IMediator, Result>(default!, default!);
        await _mediator.DidNotReceiveWithAnyArgs().Send(default(ApplyPaymentEventCommand)!, default);
    }

    [Fact]
    public async Task حدث_لمتجر_مجهول_يُقرّ_به_بلا_فعل()
    {
        Parses(new PaymentWebhookEvent("42", "pi_42", 12345));

        (await CreateHandler().Handle(new ProcessPaymentWebhookCommand("{}", "sig"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await _scopes.DidNotReceiveWithAnyArgs().RunAsync<IMediator, Result>(default!, default!);
    }
}

// تطبيق الحدث في متجره: نفس منطق التأكيد (التحقّق لدى البوّابة، مضمون التكرار).
public class ApplyPaymentEventHandlerTests
{
    private readonly IPaymentService _payment = Substitute.For<IPaymentService>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _uow = TestUnitOfWork.Create();
    private readonly Souq.Application.Features.Inventory.Contracts.IInventoryReservations _reservations =
        Substitute.For<Souq.Application.Features.Inventory.Contracts.IInventoryReservations>();

    private ApplyPaymentEventHandler CreateHandler() => new(
        _orders,
        new OrderPaymentConfirmation(_orders, _reservations,
            Substitute.For<Souq.Application.Features.Coupons.Contracts.ICouponRedemptions>(),
            Substitute.For<Souq.Application.Features.Payments.Contracts.IOrderPayments>(),
            Substitute.For<Souq.Application.Features.Baskets.Contracts.IBasketCheckout>(),
            _payment, _uow, NullLogger<OrderPaymentConfirmation>.Instance),
        NullLogger<ApplyPaymentEventHandler>.Instance);

    [Fact]
    public async Task حدث_دفعة_يؤكّد_الطلب_بعد_التحقّق_لدى_البوّابة_بلا_أي_مستخدم()
    {
        var order = new Order(customerId: 7, "عمّان", "JOD");
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.SetPaymentIntent("pi_42");
        _orders.GetWithItemsAsync(42, Arg.Any<CancellationToken>()).Returns(order);
        _payment.ConfirmAsync("pi_42", Arg.Any<CancellationToken>()).Returns(PaymentConfirmationResult.Ok());

        var result = await CreateHandler().Handle(new ApplyPaymentEventCommand("42"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _reservations.Received(1).CommitAsync(OrderStockReference.For(order.Id), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("404")]
    [InlineData("not-a-number")]
    public async Task مرجع_طلب_مجهول_يُقرّ_به_بلا_استدعاء_البوّابة(string reference)
    {
        _orders.GetWithItemsAsync(404, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var result = await CreateHandler().Handle(new ApplyPaymentEventCommand(reference), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _payment.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
