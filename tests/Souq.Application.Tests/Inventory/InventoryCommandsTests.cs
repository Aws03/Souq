using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Inventory.Commands;
using Souq.Application.Features.Inventory.Queries;
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
        _inventory.ListForProductAsync(8, Arg.Any<CancellationToken>()).Returns(new List<InventoryItem>());

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
        _inventory.ListForProductAsync(1, Arg.Any<CancellationToken>()).Returns(new List<InventoryItem> { item });

        var result = await new AdjustStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustStockCommand(1, -3, "تلف أثناء النقل"), CancellationToken.None);

        result.Value.Should().Be(new StockLevelDto(1, 7, 2, 5, 5, IsLowStock: true, VariantId: 1));
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
        _inventory.ListForProductAsync(1, Arg.Any<CancellationToken>()).Returns(new List<InventoryItem> { item });

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
        _inventory.ListForProductAsync(1, Arg.Any<CancellationToken>()).Returns(new List<InventoryItem> { item });

        var result = await new SetLowStockThresholdHandler(_inventory, Writer())
            .Handle(new SetLowStockThresholdCommand(1, 12), CancellationToken.None);

        result.Value!.LowStockThreshold.Should().Be(12);
        result.Value.IsLowStock.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── المتغيّرات (ProductVariants.md، V1) ──
    [Fact]
    public async Task مسار_المنتج_يرفض_منتجاً_بأكثر_من_متغيّر_بلا_أي_تعديل()
    {
        var small = TestCatalog.Stock(10, productId: 1, variantId: 11, id: 1);
        var large = TestCatalog.Stock(10, productId: 1, variantId: 12, id: 2);
        _inventory.ListForProductAsync(1, Arg.Any<CancellationToken>()).Returns(new List<InventoryItem> { small, large });

        var adjust = await new AdjustStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustStockCommand(1, 5, "جرد"), CancellationToken.None);
        var threshold = await new SetLowStockThresholdHandler(_inventory, Writer())
            .Handle(new SetLowStockThresholdCommand(1, 9), CancellationToken.None);

        (adjust.ErrorCode, threshold.ErrorCode).Should().Be(("VariantRequired", "VariantRequired"));
        (small.OnHand, large.OnHand, small.LowStockThreshold, large.LowStockThreshold).Should().Be((10, 10, 5, 5));
        await _movements.DidNotReceive().AddAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task مسار_المتغيّر_يصحّح_صفّه_وحده_بالقواعد_نفسها()
    {
        var large = TestCatalog.Stock(10, productId: 1, variantId: 12, id: 2);
        _inventory.GetForVariantAsync(12, Arg.Any<CancellationToken>()).Returns(large);

        var result = await new AdjustVariantStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustVariantStockCommand(12, -4, "تلف"), CancellationToken.None);
        var threshold = await new SetVariantLowStockThresholdHandler(_inventory, Writer())
            .Handle(new SetVariantLowStockThresholdCommand(12, 2), CancellationToken.None);
        var missing = await new AdjustVariantStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustVariantStockCommand(99, 1, "جرد"), CancellationToken.None);

        result.Value.Should().Be(new StockLevelDto(1, 6, 0, 6, 5, IsLowStock: false, VariantId: 12));
        threshold.Value!.LowStockThreshold.Should().Be(2);
        missing.ErrorCode.Should().Be("NotFound");
        await _movements.Received(1).AddAsync(
            Arg.Is<StockMovement>(m => m.InventoryItemId == 2 && m.QuantityChange == -4), Arg.Any<CancellationToken>());
        var refusal = () => new AdjustVariantStockHandler(_inventory, _movements, Writer())
            .Handle(new AdjustVariantStockCommand(12, -100, "جرد"), CancellationToken.None);
        await refusal.Should().ThrowAsync<InvalidInventoryOperationException>();
    }

    [Fact]
    public void أوامر_المتغيّر_مُدقَّقة_بمرجع_المتغيّر_ومدخلاتها_محروسة()
    {
        new AdjustVariantStockCommand(12, 3, "جرد").ToAuditRecord()
            .Should().BeEquivalentTo(new { Action = "inventory.adjusted", TargetType = "ProductVariant", TargetId = "12" });
        new SetVariantLowStockThresholdCommand(12, 3).ToAuditRecord()
            .Should().BeEquivalentTo(new { Action = "inventory.threshold-changed", TargetType = "ProductVariant", TargetId = "12" });
        new AdjustVariantStockValidator().Validate(new AdjustVariantStockCommand(0, 3, "جرد")).IsValid.Should().BeFalse();
        new SetVariantLowStockThresholdValidator().Validate(new SetVariantLowStockThresholdCommand(12, -1)).IsValid.Should().BeFalse();
        new GetVariantStockMovementsQueryValidator().Validate(new GetVariantStockMovementsQuery(0)).IsValid.Should().BeFalse();
    }
}
