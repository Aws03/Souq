using AwesomeAssertions;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Tests;

// مخزون المتغيّر (المرحلة 6، ADR-0026): المتاح لا يصبح سالباً، كل تغيّر في الموجود سطر سجلّ واحد (Σ السجلّ = الموجود)،
// والحجوزات صريحة تُلتزم أو تُحرَّر أو تعود — كل انتقال مرّة واحدة.
public class InventoryItemTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static InventoryItem Item(int onHand = 0, int threshold = 5)
    {
        var item = new InventoryItem(productId: 1, variantId: 1, threshold);
        if (onHand > 0) item.Receive(onHand, StockMovementType.Purchase);
        return item;
    }

    [Fact]
    public void الجديد_بلا_مخزون_ومنخفض_بحدّه_الافتراضي()
    {
        var item = Item();

        (item.OnHand, item.Reserved, item.Available, item.LowStockThreshold).Should().Be((0, 0, 0, 5));
        item.IsLowStock.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void يُفتح_لمتغيّر_منتج_محفوظ_فقط(int productId, int variantId)
    {
        var act = () => new InventoryItem(productId, variantId);

        act.Should().Throw<InvalidInventoryOperationException>();
    }

    [Fact]
    public void الاستلام_يزيد_الموجود_ويعيد_سطر_سجلّ_بالموجود_بعده()
    {
        var item = Item(3);

        var movement = item.Receive(4, StockMovementType.Return, "إرجاع عميل");

        item.OnHand.Should().Be(7);
        (movement.Type, movement.QuantityChange, movement.NewQuantity, movement.Note, movement.ProductId)
            .Should().Be((StockMovementType.Return, 4, 7, "إرجاع عميل", 1));
    }

    [Theory]
    [InlineData(0, StockMovementType.Purchase)]
    [InlineData(-1, StockMovementType.Return)]
    [InlineData(5, StockMovementType.Sale)]
    [InlineData(5, StockMovementType.Adjustment)]
    [InlineData(5, StockMovementType.Cancellation)]
    public void الاستلام_بكمية_غير_موجبة_أو_بنوع_ليس_استلاماً_يُرفض(int quantity, StockMovementType type)
    {
        var item = Item(2);

        var act = () => item.Receive(quantity, type);

        act.Should().Throw<InvalidInventoryOperationException>();
        item.OnHand.Should().Be(2);
    }

    [Fact]
    public void التصحيح_بفارق_وسبب_ولا_ينزل_بالموجود_تحت_المحجوز()
    {
        var item = Item(10);
        item.Reserve("order:1", 4, Now, "x");

        var movement = item.Adjust(-5, "  تلف  ");
        (item.OnHand, item.Available).Should().Be((5, 1));
        (movement.Type, movement.QuantityChange, movement.Note).Should().Be((StockMovementType.Adjustment, -5, "تلف"));

        ((Action)(() => item.Adjust(-2, "جرد"))).Should().Throw<InvalidInventoryOperationException>();   // 3 < 4 محجوزة
        ((Action)(() => item.Adjust(0, "جرد"))).Should().Throw<InvalidInventoryOperationException>();
        ((Action)(() => item.Adjust(1, " "))).Should().Throw<InvalidInventoryOperationException>();
        ((Action)(() => item.Adjust(1, new string('x', InventoryItem.ReasonMaxLength + 1)))).Should().Throw<InvalidInventoryOperationException>();
        item.OnHand.Should().Be(5);
    }

    [Fact]
    public void الحجز_يُنقص_المتاح_لا_الموجود()
    {
        var item = Item(5);

        var reservation = item.Reserve(" order:7 ", 2, Now.AddMinutes(30), "سماعات");

        (item.OnHand, item.Reserved, item.Available).Should().Be((5, 2, 3));
        (reservation.Reference, reservation.Quantity, reservation.Status, reservation.ExpiresAt)
            .Should().Be(("order:7", 2, ReservationStatus.Active, Now.AddMinutes(30)));
    }

    [Fact]
    public void حجز_أكثر_من_المتاح_يرفع_نفاد_المخزون_بلا_تغيير()
    {
        var item = Item(3);
        item.Reserve("order:1", 2, Now, "x");

        var act = () => item.Reserve("order:2", 2, Now, "سماعات");

        act.Should().Throw<InsufficientStockException>().Which.Message.Should().Contain("سماعات").And.Contain("1");
        item.Reserved.Should().Be(2);
        ((Action)(() => item.Reserve("order:3", 0, Now, "x"))).Should().Throw<InvalidInventoryOperationException>();
        ((Action)(() => item.Reserve(" ", 1, Now, "x"))).Should().Throw<InvalidInventoryOperationException>();
    }

    [Fact]
    public void الالتزام_يخرج_المحجوز_من_الموجود_بسطر_بيع_مرّة_واحدة()
    {
        var item = Item(5);
        var reservation = item.Reserve("order:1", 2, Now, "x");

        var sale = item.Commit(reservation, Now);
        var again = item.Commit(reservation, Now);

        (item.OnHand, item.Reserved).Should().Be((3, 0));
        (sale!.Type, sale.QuantityChange, sale.NewQuantity).Should().Be((StockMovementType.Sale, -2, 3));
        again.Should().BeNull();
        (reservation.Status, reservation.ClosedAt).Should().Be((ReservationStatus.Committed, Now));
    }

    [Fact]
    public void التحرير_يعيد_المتاح_بلا_سطر_ومرّة_واحدة()
    {
        var item = Item(5);
        var released = item.Reserve("order:1", 2, Now, "x");
        var expired = item.Reserve("order:2", 1, Now, "x");

        item.Release(released, Now).Should().BeTrue();
        item.Release(released, Now).Should().BeFalse();
        item.Release(expired, Now, expired: true).Should().BeTrue();

        (item.OnHand, item.Reserved).Should().Be((5, 0));
        released.Status.Should().Be(ReservationStatus.Released);
        expired.Status.Should().Be(ReservationStatus.Expired);
    }

    [Fact]
    public void إعادة_البيع_للموجود_لحجز_ملتزم_فقط_ومرّة_واحدة()
    {
        var item = Item(5);
        var active = item.Reserve("order:1", 1, Now, "x");
        var paid = item.Reserve("order:2", 2, Now, "x");
        item.Commit(paid, Now);

        item.Restock(active, Now, "إلغاء").Should().BeNull();       // لم يُلتزم ⇒ يُحرَّر لا يُعاد
        var back = item.Restock(paid, Now, "إلغاء طلب مدفوع");
        item.Restock(paid, Now, "مكرّر").Should().BeNull();

        (back!.Type, back.QuantityChange, back.NewQuantity).Should().Be((StockMovementType.Cancellation, 2, 5));
        paid.Status.Should().Be(ReservationStatus.Restocked);
        (item.OnHand, item.Reserved).Should().Be((5, 1));
    }

    [Fact]
    public void حجز_مخزون_آخر_يُرفض()
    {
        var item = Item(5);
        var other = Item(5);
        typeof(Souq.Domain.Common.Entity).GetProperty("Id")!.SetValue(other, 2);
        var foreign = other.Reserve("order:1", 1, Now, "x");

        ((Action)(() => item.Commit(foreign, Now))).Should().Throw<InvalidInventoryOperationException>();
        ((Action)(() => item.Release(foreign, Now))).Should().Throw<InvalidInventoryOperationException>();
        ((Action)(() => item.Restock(foreign, Now, null))).Should().Throw<InvalidInventoryOperationException>();
    }

    [Fact]
    public void السجلّ_يطابق_الموجود_عبر_كل_أنواع_التغيير()
    {
        var item = new InventoryItem(1, 1);
        var ledger = new List<StockMovement> { item.Receive(10, StockMovementType.Purchase) };
        var first = item.Reserve("order:1", 3, Now, "x");
        var second = item.Reserve("order:2", 2, Now, "x");
        ledger.Add(item.Commit(first, Now)!);
        item.Release(second, Now);
        ledger.Add(item.Adjust(-1, "جرد"));
        ledger.Add(item.Restock(first, Now, "إلغاء")!);
        ledger.Add(item.Receive(1, StockMovementType.Return));

        ledger.Sum(m => m.QuantityChange).Should().Be(item.OnHand);
        ledger.Last().NewQuantity.Should().Be(item.OnHand);
        item.Reserved.Should().Be(0);
    }

    [Fact]
    public void التنبيه_على_المتاح_والحدّ_لا_يكون_سالباً()
    {
        var item = Item(8, threshold: 5);
        item.IsLowStock.Should().BeFalse();

        item.Reserve("order:1", 3, Now, "x");     // المتاح 5 ⇒ عند الحدّ
        item.IsLowStock.Should().BeTrue();

        ((Action)(() => item.SetLowStockThreshold(-1))).Should().Throw<InvalidInventoryOperationException>();
        item.SetLowStockThreshold(0);
        item.IsLowStock.Should().BeFalse();
    }
}
