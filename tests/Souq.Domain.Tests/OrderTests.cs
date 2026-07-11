using FluentAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

public class OrderTests
{
    private static Order NewOrder() => new(customerId: 1, shippingAddress: "عمّان، شارع الملكة رانيا");

    [Fact]
    public void جديد_يبدأ_بحالة_Pending_وبلا_أسطر()
    {
        var order = NewOrder();

        order.Status.Should().Be(OrderStatus.Pending);
        order.Items.Should().BeEmpty();
        order.TotalAmount.Amount.Should().Be(0);
    }

    [Fact]
    public void AddItem_يضيف_سطراً_جديداً_ويحسب_الإجمالي()
    {
        var order = NewOrder();

        order.AddItem(productId: 1, productName: "سماعات", new Money(50), quantity: 2);

        order.Items.Should().ContainSingle();
        order.TotalAmount.Amount.Should().Be(100);
    }

    [Fact]
    public void AddItem_لنفس_المنتج_مرتين_يزيد_الكمية_بدل_تكرار_السطر()
    {
        var order = NewOrder();

        order.AddItem(productId: 1, "سماعات", new Money(50), quantity: 1);
        order.AddItem(productId: 1, "سماعات", new Money(50), quantity: 2);

        order.Items.Should().ContainSingle();
        order.Items.Single().Quantity.Should().Be(3);
        order.TotalAmount.Amount.Should().Be(150);
    }

    [Fact]
    public void AddItem_بكمية_صفر_أو_سالبة_يرفض()
    {
        var order = NewOrder();

        var act = () => order.AddItem(1, "سماعات", new Money(50), quantity: 0);

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void AddItem_بعد_الدفع_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        order.MarkAsPaid();

        var act = () => order.AddItem(2, "ساعة", new Money(30), 1);

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void MarkAsPaid_لطلب_فارغ_يُرفض()
    {
        var order = NewOrder();

        var act = () => order.MarkAsPaid();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void MarkAsPaid_لطلب_مدفوع_مسبقاً_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        order.MarkAsPaid();

        var act = () => order.MarkAsPaid();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void دورة_الحياة_الكاملة_Pending_Paid_Shipped_Delivered()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);

        order.MarkAsPaid();
        order.Status.Should().Be(OrderStatus.Paid);

        order.MarkAsShipped();
        order.Status.Should().Be(OrderStatus.Shipped);

        order.MarkAsDelivered();
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void MarkAsShipped_لطلب_لم_يُدفع_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);

        var act = () => order.MarkAsShipped();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void MarkAsDelivered_لطلب_لم_يُشحن_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        order.MarkAsPaid();

        var act = () => order.MarkAsDelivered();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    public void Cancel_مسموح_من_Pending_أو_Paid(OrderStatus status)
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        if (status == OrderStatus.Paid) order.MarkAsPaid();

        order.Cancel();

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_لطلب_مشحون_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        order.MarkAsPaid();
        order.MarkAsShipped();

        var act = () => order.Cancel();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void Cancel_لطلب_مُسلَّم_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        order.MarkAsPaid();
        order.MarkAsShipped();
        order.MarkAsDelivered();

        var act = () => order.Cancel();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void ApplyCoupon_يخصم_من_الإجمالي_النهائي_دون_مسّ_الفرعي()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 2); // فرعي = 100

        order.ApplyCoupon("SAVE10", new Money(10));

        order.Subtotal.Amount.Should().Be(100);
        order.TotalAmount.Amount.Should().Be(90);
        order.CouponCode.Should().Be("SAVE10");
    }

    [Fact]
    public void ApplyCoupon_بخصم_أكبر_من_الفرعي_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1); // فرعي = 50

        var act = () => order.ApplyCoupon("BIG", new Money(100));

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void ApplyCoupon_بعد_الدفع_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        order.MarkAsPaid();

        var act = () => order.ApplyCoupon("LATE", new Money(5));

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void SetPaymentIntent_يخزّن_المعرّف_قبل_الدفع()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);

        order.SetPaymentIntent("pi_123");

        order.PaymentIntentId.Should().Be("pi_123");
    }

    [Fact]
    public void SetPaymentIntent_بعد_الدفع_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, "سماعات", new Money(50), 1);
        order.MarkAsPaid();

        var act = () => order.SetPaymentIntent("pi_late");

        act.Should().Throw<InvalidOrderOperationException>();
    }
}
