using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Orders.Queries;
using Souq.Application.Features.Payments.Contracts;
using Souq.Application.Tests.TestDoubles;

namespace Souq.Application.Tests.Orders;

// فحص الملكية انتقل من OrdersController إلى حالة الاستخدام (Phase 0 B7). والعقد يُشكَّل للناظر (المرحلة 9): الإدارة ترى
// الملاحظات ومن غيّر الحالة وإجراءاتها المتاحة، والعميل حالته وتاريخها وإلغاءه إن جاز.
public class GetOrderByIdHandlerTests
{
    private readonly IOrderQueries _orders = Substitute.For<IOrderQueries>();
    private readonly IPaymentQueries _payments = Substitute.For<IPaymentQueries>();

    private void OrderOwnedBy(int customerId, string status = "Pending") =>
        _orders.FindAsync(1, Arg.Any<CancellationToken>()).Returns(new OrderDto(
            1, 1001, customerId, status, "عمّان", "عمّان", 50, null, null, 50, "JOD", DateTime.UnixEpoch,
            [new OrderItemDto(1, "سماعات", 50, 1, 50, 1, null, null)], null, null, new string('a', 32),
            [new OrderHistoryEntryDto("Pending", DateTime.UnixEpoch, "ملاحظة داخلية", "Staff", "موظّف")], [], false));

    [Fact]
    public async Task صاحب_الطلب_يراه_بلا_ملاحظات_الإدارة_ويستطيع_إلغاءه_قبل_الدفع()
    {
        OrderOwnedBy(customerId: 3);

        var result = await new GetOrderByIdHandler(_orders, _payments,TestCurrentUser.Customer(3))
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

        var result = await new GetOrderByIdHandler(_orders, _payments,TestCurrentUser.Customer(3))
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.Value!.CanCancel.Should().BeFalse();
    }

    [Fact]
    public async Task عميل_آخر_يرى_404_لا_403()
    {
        OrderOwnedBy(customerId: 3);

        var result = await new GetOrderByIdHandler(_orders, _payments,TestCurrentUser.Customer(4))
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task مدير_الطلبات_يرى_أي_طلب_بملاحظاته_وإجراءاته_من_جدول_الانتقالات()
    {
        OrderOwnedBy(customerId: 3, status: "Paid");

        var result = await new GetOrderByIdHandler(_orders, _payments,TestCurrentUser.Admin())
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

        var staff = await new GetOrderByIdHandler(_orders, _payments,TestCurrentUser.Staff())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        staff.Value!.History.Single().ChangedByName.Should().Be("موظّف");
    }

    [Fact]
    public async Task الإدارة_ترى_استردادات_الدفعة_وصاحب_الطلب_حالتها_وما_رُدّ_له_فقط()
    {
        OrderOwnedBy(customerId: 3, status: "Paid");
        _payments.ForOrderAsync(1, Arg.Any<CancellationToken>()).Returns(new OrderPaymentDto("Succeeded", 50, 20, 30, "JOD",
            [new RefundDto(7, 20, "Succeeded", "منتج تالف", null, DateTime.UnixEpoch, DateTime.UnixEpoch)]));

        var admin = (await new GetOrderByIdHandler(_orders, _payments, TestCurrentUser.Admin())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None)).Value!;
        var owner = (await new GetOrderByIdHandler(_orders, _payments, TestCurrentUser.Customer(3))
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None)).Value!;

        (admin.Payment!.Refunds.Single().Reason, admin.Payment.Refundable, admin.CanRefund).Should().Be(("منتج تالف", 30m, true));
        (owner.Payment!.Status, owner.Payment.RefundedAmount, owner.Payment.Refunds.Count, owner.Payment.Refundable, owner.CanRefund)
            .Should().Be(("Succeeded", 20m, 0, 0m, false));
    }

    [Theory]
    [InlineData("Succeeded", 0)]
    [InlineData("Pending", 50)]
    [InlineData("Cancelled", 50)]
    public async Task لا_استرداد_لدفعة_غير_ناجحة_أو_مستردّة_بالكامل(string status, decimal refundable)
    {
        OrderOwnedBy(customerId: 3, status: "Paid");
        _payments.ForOrderAsync(1, Arg.Any<CancellationToken>())
            .Returns(new OrderPaymentDto(status, 50, 50 - refundable, refundable, "JOD", []));

        var result = await new GetOrderByIdHandler(_orders, _payments, TestCurrentUser.Admin())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.Value!.CanRefund.Should().BeFalse();
    }

    [Fact]
    public async Task طلب_غير_موجود_404()
    {
        _orders.FindAsync(1, Arg.Any<CancellationToken>()).Returns((OrderDto?)null);

        var result = await new GetOrderByIdHandler(_orders, _payments,TestCurrentUser.Admin())
            .Handle(new GetOrderByIdQuery(1), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }
}
