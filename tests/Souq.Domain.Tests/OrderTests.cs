using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

public class OrderTests
{
    private static Order NewOrder() => new(customerId: 1, shippingAddress: "عمّان، شارع الملكة رانيا", currency: "JOD");

    [Fact]
    public void جديد_يبدأ_بحالة_Pending_وبلا_أسطر()
    {
        var order = NewOrder();

        order.Status.Should().Be(OrderStatus.Pending);
        order.Items.Should().BeEmpty();
        order.TotalAmount.Amount.Should().Be(0);
    }

    [Fact]
    public void جديد_يسجّل_سطر_تاريخ_أول_بحالة_Pending()
    {
        var order = NewOrder();

        order.StatusHistory.Should().ContainSingle();
        order.StatusHistory.Single().Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void كل_انتقال_حالة_يضيف_سطر_تاريخ_جديد()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);

        order.MarkAsPaid();
        order.MarkAsShipped();
        order.MarkAsDelivered();

        order.StatusHistory.Should().HaveCount(4); // Pending + Paid + Shipped + Delivered
        order.StatusHistory.Select(h => h.Status).Should().Equal(
            OrderStatus.Pending, OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Delivered);
    }

    [Fact]
    public void MarkAsShipped_يخزّن_رقم_التتبّع_وشركة_الشحن_والملاحظة()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();

        order.MarkAsShipped("TRK-123", "أرامكس", "سلّم للمندوب");

        order.TrackingNumber.Should().Be("TRK-123");
        order.ShippingCarrier.Should().Be("أرامكس");
        order.StatusHistory.Last().Note.Should().Be("سلّم للمندوب");
    }

    [Fact]
    public void MarkAsShipped_بلا_رقم_تتبّع_يترك_الحقل_فارغاً()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();

        order.MarkAsShipped();

        order.TrackingNumber.Should().BeNull();
        order.ShippingCarrier.Should().BeNull();
    }

    [Fact]
    public void Cancel_بملاحظة_يسجّلها_في_سطر_التاريخ()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);

        order.Cancel("فشل الدفع");

        order.StatusHistory.Last().Note.Should().Be("فشل الدفع");
    }

    [Fact]
    public void AddItem_يضيف_سطراً_جديداً_ويحسب_الإجمالي()
    {
        var order = NewOrder();

        order.AddItem(productId: 1, variantId: 1, productName: "سماعات", new Money(50, "JOD"), quantity: 2);

        order.Items.Should().ContainSingle();
        order.TotalAmount.Amount.Should().Be(100);
    }

    [Fact]
    public void AddItem_للمتغيّر_نفسه_مرتين_يزيد_الكمية_بدل_تكرار_السطر()
    {
        var order = NewOrder();

        order.AddItem(productId: 1, variantId: 1, "سماعات", new Money(50, "JOD"), quantity: 1);
        order.AddItem(productId: 1, variantId: 1, "سماعات", new Money(50, "JOD"), quantity: 2);

        order.Items.Should().ContainSingle();
        order.Items.Single().Quantity.Should().Be(3);
        order.TotalAmount.Amount.Should().Be(150);
    }

    [Fact]
    public void AddItem_لمتغيّرين_من_المنتج_نفسه_سطران_كلٌّ_بسعره_ولقطته()
    {
        var order = NewOrder();

        order.AddItem(productId: 1, variantId: 11, "قميص", new Money(20, "JOD"), quantity: 1, variantLabel: "صغير", sku: "SHIRT-S");
        order.AddItem(productId: 1, variantId: 12, "قميص", new Money(25, "JOD"), quantity: 2, variantLabel: "كبير", sku: "SHIRT-L");
        order.AddItem(productId: 1, variantId: 11, "قميص", new Money(20, "JOD"), quantity: 1, variantLabel: "صغير", sku: "SHIRT-S");

        order.Items.Select(i => (i.ProductId, i.VariantId, i.UnitPrice.Amount, i.Quantity, i.VariantLabel, i.Sku))
            .Should().Equal((1, 11, 20m, 2, "صغير", "SHIRT-S"), (1, 12, 25m, 2, "كبير", "SHIRT-L"));
        order.Subtotal.Amount.Should().Be(90);
    }

    [Fact]
    public void AddItem_بلا_لقطة_متغيّر_يحفظها_فارغة_لا_مختلقة()
    {
        var order = NewOrder();

        order.AddItem(1, 7, "سماعات", new Money(50, "JOD"), 1, variantLabel: "  ", sku: null);

        order.Items.Single().Should().BeEquivalentTo(new { VariantId = 7, VariantLabel = (string?)null, Sku = (string?)null });
    }

    [Fact]
    public void لقطة_المتغيّر_تبقى_كما_كانت_بعد_تثبيت_الطلب()
    {
        var order = NewOrder();
        order.AddItem(1, 11, "قميص", new Money(20, "JOD"), 1, variantLabel: "صغير", sku: "SHIRT-S");
        order.AssignNumber(1001);
        order.Place(new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc));

        var act = () => order.AddItem(1, 11, "قميص", new Money(18, "JOD"), 1, variantLabel: "صغير جديد", sku: "SHIRT-S2");

        act.Should().Throw<InvalidOrderOperationException>();
        order.Items.Single().Should().BeEquivalentTo(new { VariantLabel = "صغير", Sku = "SHIRT-S", Quantity = 1 });
        order.PlacedSubtotal.Should().Be(20);
    }

    [Fact]
    public void AddItem_لا_يدمج_تناقضاً_متغيّر_لمنتجين_أو_بسعرين()
    {
        var order = NewOrder();
        order.AddItem(1, 11, "قميص", new Money(20, "JOD"), 1);

        ((Action)(() => order.AddItem(2, 11, "بنطال", new Money(20, "JOD"), 1))).Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => order.AddItem(1, 11, "قميص", new Money(22, "JOD"), 1))).Should().Throw<InvalidOrderOperationException>();
        order.Items.Single().Quantity.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void AddItem_بلا_منتج_أو_متغيّر_محدّد_يرفض(int productId, int variantId)
    {
        var act = () => NewOrder().AddItem(productId, variantId, "سماعات", new Money(50, "JOD"), 1);

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void AddItem_بلقطة_أطول_من_عمودها_يرفض()
    {
        var order = NewOrder();

        ((Action)(() => order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1, variantLabel: new string('x', OrderItem.VariantLabelMaxLength + 1))))
            .Should().Throw<InvalidOrderOperationException>();
        ((Action)(() => order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1, sku: new string('X', ProductVariant.SkuMaxLength + 1))))
            .Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void AddItem_بكمية_صفر_أو_سالبة_يرفض()
    {
        var order = NewOrder();

        var act = () => order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), quantity: 0);

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void AddItem_بعد_الدفع_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();

        var act = () => order.AddItem(2, 2, "ساعة", new Money(30, "JOD"), 1);

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
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();

        var act = () => order.MarkAsPaid();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void دورة_الحياة_الكاملة_Pending_Paid_Shipped_Delivered()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);

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
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);

        var act = () => order.MarkAsShipped();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void MarkAsDelivered_لطلب_لم_يُشحن_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
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
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        if (status == OrderStatus.Paid) order.MarkAsPaid();

        order.Cancel();

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_لطلب_مشحون_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();
        order.MarkAsShipped();

        var act = () => order.Cancel();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void Cancel_لطلب_مُسلَّم_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();
        order.MarkAsShipped();
        order.MarkAsDelivered();

        var act = () => order.Cancel();

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void Cancel_لطلب_ملغى_مسبقاً_يُرفض_كي_لا_يُعاد_المخزون_مرتين()
    {
        // كل إلغاء ناجح يعني "حرّر المخزون المحجوز مرة واحدة" — إلغاء ثانٍ كان يمرّ
        // ويُكرّر سطر التاريخ (Phase 0 C10)، ومع إعادة المخزون الآن سيضخّم المخزون.
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.Cancel();

        var act = () => order.Cancel();

        act.Should().Throw<InvalidOrderOperationException>();
        order.StatusHistory.Count(h => h.Status == OrderStatus.Cancelled).Should().Be(1);
    }

    [Fact]
    public void ApplyCoupon_يخصم_من_الإجمالي_النهائي_دون_مسّ_الفرعي()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 2); // فرعي = 100

        order.ApplyCoupon("SAVE10", new Money(10, "JOD"));

        order.Subtotal.Amount.Should().Be(100);
        order.TotalAmount.Amount.Should().Be(90);
        order.CouponCode.Should().Be("SAVE10");
    }

    [Fact]
    public void ApplyCoupon_بخصم_أكبر_من_الفرعي_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1); // فرعي = 50

        var act = () => order.ApplyCoupon("BIG", new Money(100, "JOD"));

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void ApplyCoupon_بعد_الدفع_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();

        var act = () => order.ApplyCoupon("LATE", new Money(5, "JOD"));

        act.Should().Throw<InvalidOrderOperationException>();
    }

    [Fact]
    public void SetPaymentIntent_يخزّن_المعرّف_قبل_الدفع()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);

        order.SetPaymentIntent("pi_123");

        order.PaymentIntentId.Should().Be("pi_123");
    }

    [Fact]
    public void SetPaymentIntent_بعد_الدفع_يُرفض()
    {
        var order = NewOrder();
        order.AddItem(1, 1, "سماعات", new Money(50, "JOD"), 1);
        order.MarkAsPaid();

        var act = () => order.SetPaymentIntent("pi_late");

        act.Should().Throw<InvalidOrderOperationException>();
    }
}
