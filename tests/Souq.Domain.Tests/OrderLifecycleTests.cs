using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// دورة حياة الطلب (المرحلة 9): كل انتقال في مصفوفة الحالات الخمس مختبَر (المسموح ينجح ويُسجَّل، والممنوع يُرفض بلا
// أثر)، العميل يلغي قبل الدفع فقط، من فعل الانتقال يُسجَّل، والتثبيت يجمّد الأسطر والخصم والإجماليات.
public class OrderLifecycleTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    // الجدول المتوقَّع مكتوباً هنا صراحةً — لا مقروءاً من OrderTransitions — كي يختبر الجدولَ نفسه.
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> Expected =
    [
        (OrderStatus.Pending, OrderStatus.Paid), (OrderStatus.Pending, OrderStatus.Cancelled),
        (OrderStatus.Paid, OrderStatus.Shipped), (OrderStatus.Paid, OrderStatus.Cancelled),
        (OrderStatus.Shipped, OrderStatus.Delivered),
    ];

    public static TheoryData<OrderStatus, OrderStatus> AllPairs()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var from in Enum.GetValues<OrderStatus>())
            foreach (var to in Enum.GetValues<OrderStatus>())
                data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void كل_انتقال_مسموح_ينجح_ويُسجَّل_وكل_ممنوع_يُرفض_بلا_أثر(OrderStatus from, OrderStatus to)
    {
        var order = OrderIn(from);
        var history = order.StatusHistory.Count;
        var allowed = Expected.Contains((from, to));

        var move = () => MoveTo(order, to);

        OrderTransitions.CanMove(from, to, OrderActor.System).Should().Be(allowed);
        if (allowed)
        {
            move();
            order.Status.Should().Be(to);
            order.StatusHistory.Should().HaveCount(history + 1);
            order.StatusHistory.Last().Status.Should().Be(to);
        }
        else
        {
            move.Should().Throw<InvalidOrderOperationException>();
            order.Status.Should().Be(from);
            order.StatusHistory.Should().HaveCount(history);
        }
    }

    [Fact]
    public void العميل_يلغي_قبل_الدفع_فقط_ويُسجَّل_أنه_العميل()
    {
        var pending = OrderIn(OrderStatus.Pending);
        pending.Cancel("غيّرت رأيي", OrderActor.Customer(7));
        pending.StatusHistory.Last().Should().BeEquivalentTo(new
        {
            Status = OrderStatus.Cancelled, ChangedBy = OrderActorKind.Customer, ChangedByUserId = (int?)7, Note = "غيّرت رأيي",
        }, o => o.ExcludingMissingMembers());

        var paid = OrderIn(OrderStatus.Paid);
        ((Action)(() => paid.Cancel(null, OrderActor.Customer(7)))).Should().Throw<InvalidOrderOperationException>();
        paid.Status.Should().Be(OrderStatus.Paid);

        OrderTransitions.CustomerCanCancel(OrderStatus.Pending).Should().BeTrue();
        new[] { OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Delivered, OrderStatus.Cancelled }
            .Should().OnlyContain(s => !OrderTransitions.CustomerCanCancel(s));
    }

    [Fact]
    public void العميل_لا_يدفع_ولا_يشحن_ولا_يسلّم()
    {
        ((Action)(() => OrderIn(OrderStatus.Pending).MarkAsPaid(by: OrderActor.Customer()))).Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => OrderIn(OrderStatus.Paid).MarkAsShipped(by: OrderActor.Customer()))).Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => OrderIn(OrderStatus.Shipped).MarkAsDelivered(by: OrderActor.Customer()))).Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void من_غيّر_الحالة_يُسجَّل_مع_كل_سطر()
    {
        var order = NewPlacedOrder();
        order.MarkAsPaid(by: OrderActor.PaymentGateway);
        order.MarkAsShipped("TRK", null, "سلّم للمندوب", OrderActor.Staff(42));
        order.MarkAsDelivered();

        order.StatusHistory.Select(h => (h.Status, h.ChangedBy, h.ChangedByUserId)).Should().Equal(
            (OrderStatus.Pending, OrderActorKind.Customer, (int?)null),
            (OrderStatus.Paid, OrderActorKind.PaymentGateway, (int?)null),
            (OrderStatus.Shipped, OrderActorKind.Staff, (int?)42),
            (OrderStatus.Delivered, OrderActorKind.System, (int?)null));
        ((Action)(() => OrderActor.Staff(0))).Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void التثبيت_يجمّد_الأسطر_والخصم_ويحفظ_الإجماليات()
    {
        var order = NewOrder();
        order.AssignNumber(1001);
        order.AddItem(1, "سماعات", new Money(50, "JOD"), 2);
        order.ApplyCoupon("SAVE10", new Money(10, "JOD"));

        order.Place(Now);

        (order.IsPlaced, order.PlacedAt, order.PlacedSubtotal, order.PlacedTotal).Should().Be((true, Now, 100m, 90m));
        ((Action)(() => order.AddItem(2, "شاحن", new Money(5, "JOD"), 1))).Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => order.ApplyCoupon("MORE", new Money(20, "JOD")))).Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => order.Place(Now))).Should().Throw<InvalidOrderOperationException>();
        (order.Subtotal.Amount, order.TotalAmount.Amount).Should().Be((100m, 90m));

        // بعد الدفع الإجماليات هي نفسها — الانتقالات لا تمسّها.
        order.MarkAsPaid();
        (order.PlacedTotal, order.TotalAmount.Amount).Should().Be((90m, 90m));
    }

    [Fact]
    public void التثبيت_يتطلّب_رقماً_وأسطراً_والرقم_يُعيَّن_مرة()
    {
        var empty = NewOrder();
        empty.AssignNumber(1001);
        ((Action)(() => empty.Place(Now))).Should().Throw<InvalidOrderOperationException>();

        var unnumbered = NewOrder();
        unnumbered.AddItem(1, "سماعات", new Money(50, "JOD"), 1);
        ((Action)(() => unnumbered.Place(Now))).Should().Throw<InvalidOrderOperationException>();

        unnumbered.AssignNumber(1002);
        ((Action)(() => unnumbered.AssignNumber(1003))).Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => NewOrder().AssignNumber(0))).Should().Throw<InvalidOrderOperationException>();
        unnumbered.OrderNumber.Should().Be(1002);
    }

    [Fact]
    public void رمز_التتبّع_عشوائي_بطول_ثابت_وعنوان_الفوترة_لقطة()
    {
        var first = NewOrder();
        var second = NewOrder();

        first.TrackingToken.Should().MatchRegex($"^[0-9a-f]{{{Order.TrackingTokenLength}}}$");
        first.TrackingToken.Should().NotBe(second.TrackingToken);

        first.BillingAddress.Should().Be(first.ShippingAddress);
        new Order(1, "عمّان", "JOD", billingAddress: "  إربد، شارع الجامعة  ").BillingAddress.Should().Be("إربد، شارع الجامعة");
        ((Action)(() => new Order(1, "عمّان", "JOD", billingAddress: " "))).Should().Throw<InvalidOrderOperationException>();
    }

    private static Order NewOrder() => new(customerId: 1, shippingAddress: "عمّان", currency: "JOD");

    private static Order NewPlacedOrder()
    {
        var order = NewOrder();
        order.AssignNumber(1001);
        order.AddItem(1, "سماعات", new Money(50, "JOD"), 1);
        order.Place(Now);
        return order;
    }

    // طلب في الحالة المطلوبة عبر الانتقالات المشروعة نفسها.
    private static Order OrderIn(OrderStatus status)
    {
        var order = NewPlacedOrder();
        switch (status)
        {
            case OrderStatus.Paid: order.MarkAsPaid(); break;
            case OrderStatus.Shipped: order.MarkAsPaid(); order.MarkAsShipped(); break;
            case OrderStatus.Delivered: order.MarkAsPaid(); order.MarkAsShipped(); order.MarkAsDelivered(); break;
            case OrderStatus.Cancelled: order.Cancel(); break;
        }
        return order;
    }

    // لا دالة تعيد طلباً إلى Pending — الانتقال إليها ممنوع دائماً.
    private static void MoveTo(Order order, OrderStatus target)
    {
        switch (target)
        {
            case OrderStatus.Pending: throw new InvalidOrderOperationException("لا عودة إلى Pending");
            case OrderStatus.Paid: order.MarkAsPaid(); break;
            case OrderStatus.Shipped: order.MarkAsShipped(); break;
            case OrderStatus.Delivered: order.MarkAsDelivered(); break;
            case OrderStatus.Cancelled: order.Cancel(); break;
        }
    }
}
