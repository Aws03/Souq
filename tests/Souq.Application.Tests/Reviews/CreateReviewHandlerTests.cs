using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Reviews.Commands;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.Reviews;

// تنسيق حالة الاستخدام: المقيِّم هو المستخدم الحالي، لا تقييم مكرّر، ولا تقييم بلا طلب مُسلَّم.
// "مُسلَّم يحوي المنتج" نفسه استعلام SQL (FindDeliveredOrderIdContainingAsync) يُثبَت على SQL
// Server في اختبارات التكامل (طلب مدفوع غير مُسلَّم ⇒ لا أحقّية). النشر (المرحلة 13) بسياسة متجر السياق.
public class CreateReviewHandlerTests
{
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Customer _customer = new(userId: 1, "عميل", "c@souq.test");
    private readonly Tenant _store = new("متجر اختبار", "test-store", "JOD", "ar", "Asia/Amman");

    public CreateReviewHandlerTests()
    {
        _customers.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_customer);
        _tenants.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_store);
    }

    private CreateReviewHandler CreateHandler(TestCurrentUser? user = null) =>
        new(_reviews, _orders, _customers, _tenants, TestTenant.Context(id: 1), user ?? TestCurrentUser.Customer(1), _uow);

    [Fact]
    public async Task عميل_محظور_لا_يقيّم_حتى_لو_استلم()
    {
        // المرحلة 7: الحظر التجاري يمنع التقييم كما يمنع الشراء.
        _customer.Block(DateTime.UtcNow);
        _orders.FindDeliveredOrderIdContainingAsync(1, 5, Arg.Any<CancellationToken>()).Returns(9);

        var result = await CreateHandler().Handle(new CreateReviewCommand(ProductId: 5, Rating: 5, Comment: "ممتاز"), CancellationToken.None);

        result.ErrorCode.Should().Be("CustomerBlocked");
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task عميل_بلا_طلب_مُسلَّم_يحوي_المنتج_يُرفض()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);
        _orders.FindDeliveredOrderIdContainingAsync(1, 5, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await CreateHandler().Handle(
            new CreateReviewCommand(ProductId: 5, Rating: 5, Comment: "ممتاز"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotEligible");
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task عميل_قيّم_المنتج_مسبقاً_يُرفض_بلا_فحص_الطلبات()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(new CreateReviewCommand(5, 5, "ممتاز"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("AlreadyReviewed");
        await _orders.DidNotReceive().FindDeliveredOrderIdContainingAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task طلب_مُسلَّم_يحوي_المنتج_يُتيح_التقييم_باسم_المستخدم_الحالي_ويربطه_بالطلب()
    {
        _reviews.HasCustomerReviewedProductAsync(1, 5, Arg.Any<CancellationToken>()).Returns(false);
        _orders.FindDeliveredOrderIdContainingAsync(1, 5, Arg.Any<CancellationToken>()).Returns(42);

        var result = await CreateHandler().Handle(new CreateReviewCommand(5, 5, "منتج رائع"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _reviews.Received(1).AddAsync(
            Arg.Is<Review>(r => r.ProductId == 5 && r.CustomerId == 1 && r.OrderId == 42 && r.Rating == 5),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task متجر_بالإشراف_المسبق_يحفظ_التقييم_معلّقاً_ويخبر_العميل()
    {
        _orders.FindDeliveredOrderIdContainingAsync(1, 5, Arg.Any<CancellationToken>()).Returns(42);

        var result = await CreateHandler().Handle(new CreateReviewCommand(5, 4, "جيد"), CancellationToken.None);

        result.Value!.Status.Should().Be("Pending");
        await _reviews.Received(1).AddAsync(Arg.Is<Review>(r => r.Status == ReviewStatus.Pending), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task متجر_بالاعتماد_التلقائي_ينشر_التقييم_فوراً()
    {
        _store.SetReviewsAutoApprove(true);
        _orders.FindDeliveredOrderIdContainingAsync(1, 5, Arg.Any<CancellationToken>()).Returns(42);

        var result = await CreateHandler().Handle(new CreateReviewCommand(5, 4, "جيد"), CancellationToken.None);

        result.Value!.Status.Should().Be("Approved");
        await _reviews.Received(1).AddAsync(Arg.Is<Review>(r => r.IsPublished), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task بلا_مستخدم_مُصادَق_لا_يُكتب_تقييم()
    {
        var act = () => CreateHandler(TestCurrentUser.Anonymous()).Handle(new CreateReviewCommand(5, 5, "x"), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationRequiredException>();
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }
}
