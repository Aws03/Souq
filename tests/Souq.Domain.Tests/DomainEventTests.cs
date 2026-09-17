using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Events;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// أحداث المجال (المرحلة 14): الطلب يرفع انتقالاته بعد الإنشاء، والمخزون يرفع عبور حدّ التنبيه نزولاً مرّة لكل هبوط — لكيان محفوظ
// فقط. الإشعار داخل التطبيق يحرس مستلمه ونوعه وحجم بياناته، والقراءة مرّة واحدة.
public class DomainEventTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static T Saved<T>(T entity, int id) where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }

    private static Order PaidReadyOrder(int id = 9)
    {
        var order = new Order(customerId: 3, "عمّان", "JOD");
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        return id > 0 ? Saved(order, id) : order;
    }

    [Fact]
    public void انتقالات_الطلب_المحفوظ_ترفع_حدثاً_بالحالتين_والفاعل()
    {
        var order = PaidReadyOrder();
        order.PendingDomainEvents().Should().BeEmpty("الإنشاء نفسه ليس انتقالاً");

        order.MarkAsPaid(by: OrderActor.PaymentGateway);
        order.MarkAsShipped(by: OrderActor.Staff(5));

        order.PendingDomainEvents().Should().Equal(
            new OrderStatusChanged(9, 3, OrderStatus.Pending, OrderStatus.Paid, OrderActorKind.PaymentGateway),
            new OrderStatusChanged(9, 3, OrderStatus.Paid, OrderStatus.Shipped, OrderActorKind.Staff));

        order.ClearDomainEvents();
        order.PendingDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void طلب_لم_يُحفظ_ولا_انتقال_مرفوض_لا_يرفعان_حدثاً()
    {
        var unsaved = PaidReadyOrder(id: 0);
        unsaved.Cancel(by: OrderActor.System);
        unsaved.PendingDomainEvents().Should().BeEmpty();

        var order = PaidReadyOrder();
        var ship = () => order.MarkAsShipped();
        ship.Should().Throw<InvalidOrderOperationException>();
        order.PendingDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void عبور_حدّ_التنبيه_نزولاً_حدث_واحد_لكل_هبوط()
    {
        var item = Saved(new InventoryItem(productId: 4, variantId: 4, lowStockThreshold: 5), 11);
        item.Receive(7, StockMovementType.Purchase);

        item.Reserve("order:1", 1, Now.AddMinutes(30), "سماعات");   // 7 ⇒ 6: فوق الحدّ
        item.PendingDomainEvents().Should().BeEmpty();

        item.Reserve("order:2", 1, Now.AddMinutes(30), "سماعات");   // 6 ⇒ 5: عبور
        item.Reserve("order:3", 1, Now.AddMinutes(30), "سماعات");   // 5 ⇒ 4: ما زال تحته — لا حدث ثانٍ
        item.PendingDomainEvents().Should().Equal(new StockBecameLow(4, 4, 5, 5));

        item.ClearDomainEvents();
        item.Adjust(10, "توريد");   // صعود فوق الحدّ
        item.Adjust(-12, "تلف");    // 14 ⇒ 2: هبوط جديد
        item.PendingDomainEvents().Should().Equal(new StockBecameLow(4, 4, 2, 5));
    }

    [Fact]
    public void مخزون_لم_يُحفظ_لا_يرفع_حدثاً()
    {
        var item = new InventoryItem(productId: 4, variantId: 4, lowStockThreshold: 5);
        item.Receive(6, StockMovementType.Purchase);

        item.Reserve("order:1", 2, Now, "سماعات");

        item.PendingDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void الإشعار_يحرس_مستلمه_ونوعه_وبياناته_والقراءة_مرّة()
    {
        var noRecipient = () => new Notification(0, NotificationKinds.OrderStatus, "{}");
        var noKind = () => new Notification(7, " ", "{}");
        var hugeData = () => new Notification(7, NotificationKinds.LowStock, new string('x', Notification.DataMaxLength + 1));
        noRecipient.Should().Throw<InvalidNotificationException>();
        noKind.Should().Throw<InvalidNotificationException>();
        hugeData.Should().Throw<InvalidNotificationException>();

        var notification = new Notification(7, NotificationKinds.NewOrder, "{\"orderNumber\":\"1001\"}");
        notification.IsRead.Should().BeFalse();
        notification.MarkRead(Now);
        notification.MarkRead(Now.AddHours(1));
        notification.ReadAt.Should().Be(Now);
    }
}
