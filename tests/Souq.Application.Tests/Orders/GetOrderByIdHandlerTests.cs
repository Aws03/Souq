using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Orders.Queries;
using Souq.Application.Tests.TestDoubles;

namespace Souq.Application.Tests.Orders;

// فحص الملكية انتقل من OrdersController إلى حالة الاستخدام (Phase 0 B7). والعقد يُشكَّل للناظر (المرحلة 9): الإدارة ترى
// الملاحظات ومن غيّر الحالة وإجراءاتها المتاحة، والعميل حالته وتاريخها وإلغاءه إن جاز.
public class GetOrderByIdHandlerTests
{
    private readonly IOrderQueries _orders = Substitute.For<IOrderQueries>();

    private void OrderOwnedBy(int customerId, string status = "Pending") =>
        _orders.FindAsync(1, Arg.Any<CancellationToken>()).Returns(new OrderDto(
            1, 1001, customerId, status, "عمّان", "عمّان", 50, null, null, 50, "JOD", DateTime.UnixEpoch,
            [new OrderItemDto(1, "سماعات", 50, 1, 50)], null, null, new string('a', 32),
            [new OrderHistoryEntryDto("Pending", DateTime.UnixEpoch, "ملاحظة داخلية", "Staff", "موظّف")], [], false));

    [Fact]
    public async Task صاحب_الطلب_يراه_بلا_ملاحظات_الإدارة_ويستطيع_إلغاءه_قبل_الدفع()
    {
        OrderOwnedBy(customerId: 3);

        var result = await new GetOrderByIdHandler(_orders, TestCurrentUser.Customer(3))
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        var order = result.Value!;
        (order.CustomerId, order.CanCancel, order.AllowedActions.Count).Should().Be((3, true, 0));
        order.History.Single().Should().BeEquivalentTo(new { Status = "Pending", Note = (string?)null, ChangedBy = (string?)null, ChangedByName = (string?)null },
            o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task الطلب_المدفوع_لا_يلغيه_العميل()
    {
        OrderOwnedBy(customerId: 3, status: "Paid");

        var result = await new GetOrderByIdHandler(_orders, TestCurrentUser.Customer(3))
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.Value!.CanCancel.Should().BeFalse();
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
    public async Task مدير_الطلبات_يرى_أي_طلب_بملاحظاته_وإجراءاته_من_جدول_الانتقالات()
    {
        OrderOwnedBy(customerId: 3, status: "Paid");

        var result = await new GetOrderByIdHandler(_orders, TestCurrentUser.Admin())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        var order = result.Value!;
        order.AllowedActions.Should().Equal("Ship", "Cancel");
        order.CanCancel.Should().BeFalse();
        order.History.Single().Note.Should().Be("ملاحظة داخلية");
    }

    [Fact]
    public async Task الموظّف_بعرض_بلا_إدارة_يرى_ولا_إجراءات_له()
    {
        OrderOwnedBy(customerId: 3, status: "Paid");

        var staff = await new GetOrderByIdHandler(_orders, TestCurrentUser.Staff())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        staff.Value!.History.Single().ChangedByName.Should().Be("موظّف");
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
