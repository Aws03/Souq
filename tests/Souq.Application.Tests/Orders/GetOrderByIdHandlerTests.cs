using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Orders.Queries;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Orders;

// فحص الملكية انتقل من OrdersController إلى حالة الاستخدام (Phase 0 B7).
public class GetOrderByIdHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();

    private Order OrderOwnedBy(int customerId)
    {
        var order = new Order(customerId, "عمّان");
        order.AddItem(1, "سماعات", new Money(50), 1);
        _orders.GetWithItemsAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

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
}
