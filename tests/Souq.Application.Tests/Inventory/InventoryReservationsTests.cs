using AwesomeAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Inventory.Reservations;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Inventory;

// عقود Inventory (المرحلة 6): الحجز كلّه أو لا شيء، الالتزام والإلغاء مضمونا التكرار، وتعارض التزامن يُعاد من قراءة
// جديدة حتى حدّ — فالمحاولة التالية ترى المتاح الحقيقي. سلوك rowversion ونقاط الحفظ نفسها تثبته اختبارات التكامل.
public class InventoryReservationsTests
{
    private readonly IInventoryRepository _inventory = Substitute.For<IInventoryRepository>();
    private readonly IStockMovementRepository _movements = Substitute.For<IStockMovementRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = new();

    private InventoryReservations Create() => new(
        _inventory, _movements, new InventoryWriter(_inventory, _uow), new InventorySettings { ReservationMinutes = 30 }, _clock);

    private void Items(params InventoryItem[] items) =>
        _inventory.GetByVariantsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(items);

    private void Reservations(string reference, InventoryItem item, params StockReservation[] reservations)
    {
        _inventory.GetReservationsAsync(reference, Arg.Any<CancellationToken>()).Returns(reservations);
        _inventory.GetByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[] { item });
    }

    [Fact]
    public async Task يحجز_بمهلة_من_الإعدادات_ويحفظ_مرّة()
    {
        var item = TestCatalog.Stock(10);
        Items(item);
        StockReservation? added = null;
        _inventory.When(i => i.AddReservationAsync(Arg.Any<StockReservation>(), Arg.Any<CancellationToken>()))
            .Do(call => added = call.Arg<StockReservation>());

        await Create().ReserveAsync("order:5", [new ReservationLine(1, 3, "سماعات")], CancellationToken.None);

        (item.OnHand, item.Reserved, item.Available).Should().Be((10, 3, 7));
        added!.Reference.Should().Be("order:5");
        added.ExpiresAt.Should().Be(_clock.UtcNow.AddMinutes(30));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task سطران_لنفس_المتغيّر_حجز_واحد_بمجموعهما()
    {
        var item = TestCatalog.Stock(5);
        Items(item);

        await Create().ReserveAsync("order:5", [new ReservationLine(1, 2, "x"), new ReservationLine(1, 3, "x")], CancellationToken.None);

        item.Reserved.Should().Be(5);
        await _inventory.Received(1).AddReservationAsync(Arg.Is<StockReservation>(r => r.Quantity == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task نقص_في_سطر_يرفض_الكلّ_بلا_حفظ_وينسى_التغييرات_الجزئية()
    {
        Items(TestCatalog.Stock(10, variantId: 1, id: 1), TestCatalog.Stock(1, productId: 2, variantId: 2, id: 2));

        var act = () => Create().ReserveAsync("order:5",
            [new ReservationLine(1, 2, "أ"), new ReservationLine(2, 5, "ب")], CancellationToken.None);

        (await act.Should().ThrowAsync<InsufficientStockException>()).Which.Message.Should().Contain("ب");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _inventory.Received(1).Reset();
    }

    [Fact]
    public async Task تعارض_تزامن_يعيد_القراءة_ثم_ينجح()
    {
        var stale = TestCatalog.Stock(1);
        var fresh = TestCatalog.Stock(1);
        _inventory.GetByVariantsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { stale }, new[] { fresh });
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new ConcurrencyConflictException(), _ => Task.FromResult(1));

        await Create().ReserveAsync("order:5", [new ReservationLine(1, 1, "x")], CancellationToken.None);

        _inventory.Received(1).Reset();
        fresh.Reserved.Should().Be(1);
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تعارض_متكرّر_يُرفع_بعد_أقصى_المحاولات()
    {
        _inventory.GetByVariantsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new[] { TestCatalog.Stock(100) });
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());

        var act = () => Create().ReserveAsync("order:5", [new ReservationLine(1, 1, "x")], CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await _uow.Received(InventoryWriter.MaxAttempts).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الالتزام_يحوّل_النشط_بيعاً_مرّة_واحدة()
    {
        var item = TestCatalog.Stock(10);
        var reservation = item.Reserve("order:5", 3, _clock.UtcNow.AddMinutes(30), "x");
        Reservations("order:5", item, reservation);

        await Create().CommitAsync("order:5", CancellationToken.None);
        await Create().CommitAsync("order:5", CancellationToken.None);

        (item.OnHand, item.Reserved).Should().Be((7, 0));
        reservation.Status.Should().Be(ReservationStatus.Committed);
        await _movements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Sale && m.QuantityChange == -3 && m.NewQuantity == 7),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task الإلغاء_يحرّر_النشط_ويعيد_الملتزم_للموجود_بحركة_إلغاء()
    {
        var item = TestCatalog.Stock(10);
        var active = item.Reserve("order:5", 2, _clock.UtcNow.AddMinutes(30), "x");
        var committed = item.Reserve("order:5", 3, _clock.UtcNow.AddMinutes(30), "x");
        item.Commit(committed, _clock.UtcNow);
        Reservations("order:5", item, active, committed);

        await Create().CancelAsync("order:5", "إلغاء", expired: false, CancellationToken.None);

        (item.OnHand, item.Reserved).Should().Be((10, 0));
        active.Status.Should().Be(ReservationStatus.Released);
        committed.Status.Should().Be(ReservationStatus.Restocked);
        await _movements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Cancellation && m.QuantityChange == 3 && m.Note == "إلغاء"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task انتهاء_المهلة_يعلّم_الحجز_منتهياً()
    {
        var item = TestCatalog.Stock(4);
        var active = item.Reserve("order:5", 4, _clock.UtcNow, "x");
        Reservations("order:5", item, active);

        await Create().CancelAsync("order:5", "انتهت المهلة", expired: true, CancellationToken.None);

        active.Status.Should().Be(ReservationStatus.Expired);
        item.Available.Should().Be(4);
        await _movements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task المتاح_لكل_متغيّر_هو_الموجود_ناقص_المحجوز()
    {
        var item = TestCatalog.Stock(6);
        item.Reserve("order:1", 2, _clock.UtcNow, "x");
        Items(item);

        var available = await Create().AvailableAsync([1], CancellationToken.None);

        available.Should().Equal(new Dictionary<int, int> { [1] = 4 });
    }
}
