using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// لقطة الشحن على الطلب (المرحلة 12): الإجمالي = الفرعي − الخصم + الشحن، يُثبَّت مع الطلب ولا يتغيّر بعده؛ ناقل الطريقة
// يبقى ناقل الشحنة ما لم يُحدَّد غيره، ورابط التتبّع يظهر برقم الشحنة. طلب بلا شحن إجماليه كما كان.
// ============================================================================
public class OrderShippingTests
{
    private static Order NewOrder()
    {
        var order = new Order(customerId: 1, "عمّان", "JOD");
        order.AddItem(1, 1, "سماعات", new Money(20, "JOD"), 2);
        return order;
    }

    [Fact]
    public void طلب_بلا_شحن_إجماليه_كما_كان()
    {
        var order = NewOrder();

        (order.TotalAmount, order.ShippingCost, order.ShippingMethodName).Should().Be((new Money(40, "JOD"), Money.Zero("JOD"), (string?)null));
    }

    [Fact]
    public void الشحن_يُضاف_للإجمالي_ويُثبَّت_مع_الطلب()
    {
        var order = NewOrder();
        order.ApplyCoupon("SAVE", new Money(4, "JOD"));

        order.ApplyShipping("توصيل سريع", new Money(3.5m, "JOD"), "Aramex", "https://track.example/{number}", 1, 2, "jo");

        (order.TotalAmount, order.ShippingCountry, order.ShippingCarrier, order.ShippingMinDays, order.ShippingMaxDays)
            .Should().Be((new Money(39.5m, "JOD"), "JO", "Aramex", (int?)1, (int?)2));
        order.AssignNumber(1001);
        order.Place(DateTime.UtcNow);
        order.PlacedTotal.Should().Be(39.5m);
        order.Invoking(o => o.ApplyShipping("أخرى", new Money(1, "JOD"), null, null, null, null, null))
            .Should().Throw<InvalidOrderOperationException>("الطلب مثبَّت");
    }

    [Fact]
    public void رابط_التتبّع_برقم_الشحنة_وناقل_الطريقة_يبقى_إن_لم_يُحدَّد_غيره()
    {
        var order = NewOrder();
        order.ApplyShipping("توصيل", new Money(2, "JOD"), "Aramex", "https://track.example/{number}", null, null, null);
        order.AssignNumber(1001);
        order.Place(DateTime.UtcNow);
        order.SetPaymentIntent("pi_1");
        order.MarkAsPaid();
        order.TrackingUrl.Should().BeNull("لا رقم قبل الشحن");

        order.MarkAsShipped("ABC 1");

        (order.ShippingCarrier, order.TrackingUrl).Should().Be(("Aramex", "https://track.example/ABC%201"));
    }

    [Fact]
    public void ناقل_صريح_عند_الشحن_يغلب_ناقل_الطريقة()
    {
        var order = NewOrder();
        order.ApplyShipping("توصيل", new Money(2, "JOD"), "Aramex", null, null, null, null);
        order.MarkAsPaid();

        order.MarkAsShipped("X1", "DHL");

        order.ShippingCarrier.Should().Be("DHL");
    }

    [Fact]
    public void لقطة_الشحن_بعملة_الطلب_واسم_ودولة_بحرفين()
    {
        var order = NewOrder();

        order.Invoking(o => o.ApplyShipping("توصيل", new Money(2, "USD"), null, null, null, null, null))
            .Should().Throw<InvalidOrderOperationException>();
        order.Invoking(o => o.ApplyShipping("توصيل", new Money(2, "JOD"), null, null, null, null, "JOR"))
            .Should().Throw<InvalidOrderOperationException>();
        order.Invoking(o => o.ApplyShipping(" ", new Money(2, "JOD"), null, null, null, null, null))
            .Should().Throw<InvalidOrderOperationException>();
        order.TotalAmount.Should().Be(new Money(40, "JOD"));
    }
}
