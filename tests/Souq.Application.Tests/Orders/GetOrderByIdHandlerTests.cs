using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Orders.Queries;
using Souq.Application.Tests.TestDoubles;

namespace Souq.Application.Tests.Orders;

// فحص الملكية انتقل من OrdersController إلى حالة الاستخدام (Phase 0 B7).
public class GetOrderByIdHandlerTests
{
    private readonly IOrderQueries _orders = Substitute.For<IOrderQueries>();

    private void OrderOwnedBy(int customerId) =>
        _orders.FindAsync(1, Arg.Any<CancellationToken>()).Returns(new OrderDto(
            1, customerId, "Pending", "عمّان", 50, null, null, 50, "JOD", DateTime.UnixEpoch,
            [new OrderItemDto(1, "سماعات", 50, 1, 50)], null, null));

    [Fact]
    public async Task صاحب_الطلب_يراه()
    {
        OrderOwnedBy(customerId: 3);

        var result = await new GetOrderByIdHandler(_orders, TestCurrentUser.Customer(3))
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CustomerId.Should().Be(3);
    }

    [Fact]
    public async Task عميل_آخر_يرى_404_لا_403()
    {
        OrderOwnedBy(customerId: 3);

        var result = await new GetOrderByIdHandler(_orders, TestCurrentUser.Customer(4))
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task مدير_الطلبات_يرى_أي_طلب()
    {
        OrderOwnedBy(customerId: 3);

        var result = await new GetOrderByIdHandler(_orders, TestCurrentUser.Admin())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task طلب_غير_موجود_404()
    {
        _orders.FindAsync(1, Arg.Any<CancellationToken>()).Returns((OrderDto?)null);

        var result = await new GetOrderByIdHandler(_orders, TestCurrentUser.Admin())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }
}
