using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Inventory.Commands;
using Souq.Application.Features.Inventory.Reservations;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Inventory;

// تصحيح المخزون بفارق وسبب (يحلّ محلّ تعيين المخزون المطلق — Phase 0 C4) وحدّ التنبيه، من وحدة Inventory.
public class InventoryCommandsTests
{
    private readonly IInventoryRepository _inventory = Substitute.For<IInventoryRepository>();
    private readonly IStockMovementRepository _movements = Substitute.For<IStockMovementRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private InventoryWriter Writer() => new(_inventory, _uow);

    [Fact]
    public async Task منتج_بلا_مخزون_في_المتجر_غير_موجود()
    {
        _inventory.GetForProductAsync(8, Arg.Any<CancellationToken>()).Returns((InventoryItem?)null);

        var result = await new AdjustStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustStockCommand(8, 5, "جرد"), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
        await _movements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task التصحيح_يطبّق_الفارق_على_القيمة_الحالية_ويسجّل_حركة_بسببه()
    {
        var item = TestCatalog.Stock(10);
        item.Reserve("order:1", 2, DateTime.UtcNow, "x");
        _inventory.GetForProductAsync(1, Arg.Any<CancellationToken>()).Returns(item);

        var result = await new AdjustStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustStockCommand(1, -3, "تلف أثناء النقل"), CancellationToken.None);

        result.Value.Should().Be(new StockLevelDto(1, 7, 2, 5, 5, IsLowStock: true));
        await _movements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.Type == StockMovementType.Adjustment && m.QuantityChange == -3 && m.Note == "تلف أثناء النقل"),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task تصحيح_ينزل_بالموجود_تحت_المحجوز_يُرفض_بلا_حفظ()
    {
        var item = TestCatalog.Stock(5);
        item.Reserve("order:1", 4, DateTime.UtcNow, "x");
        _inventory.GetForProductAsync(1, Arg.Any<CancellationToken>()).Returns(item);

        var act = () => new AdjustStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustStockCommand(1, -2, "جرد"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidInventoryOperationException>();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _inventory.Received(1).Reset();
    }

    [Fact]
    public async Task حدّ_التنبيه_يُضبط_من_وحدة_المخزون()
    {
        var item = TestCatalog.Stock(10);
        _inventory.GetForProductAsync(1, Arg.Any<CancellationToken>()).Returns(item);

        var result = await new SetLowStockThresholdHandler(_inventory, Writer())
            .Handle(new SetLowStockThresholdCommand(1, 12), CancellationToken.None);

        result.Value!.LowStockThreshold.Should().Be(12);
        result.Value.IsLowStock.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
