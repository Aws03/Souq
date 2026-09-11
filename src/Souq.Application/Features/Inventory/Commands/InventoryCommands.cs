using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Features.Inventory.Reservations;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Inventory.Commands;

// مستوى مخزون منتج بعد عملية إدارة — تعيده الأوامر كي تحدّث الواجهة سطرها بلا طلب جديد.
public record StockLevelDto(int ProductId, int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLowStock)
{
    internal static StockLevelDto From(InventoryItem item) =>
        new(item.ProductId, item.OnHand, item.Reserved, item.Available, item.LowStockThreshold, item.IsLowStock);
}

// ============================================================================
// تصحيح مخزون منتج بفارق وسبب (inventory.manage، مُدقَّق). يحلّ محلّ تعيين المخزون المطلق من نموذج المنتج
// (Phase 0 C4): الفارق يُطبَّق على القيمة الحالية، فبيع متزامن لا يُمحى. القواعد في InventoryItem.Adjust.
// ============================================================================
public record AdjustStockCommand(int ProductId, int Delta, string Reason) : IRequest<Result<StockLevelDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("inventory.adjusted", "Product", ProductId.ToString(),
        Metadata: new Dictionary<string, object?> { ["delta"] = Delta, ["reason"] = Reason });
}

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Delta).NotEqual(0).InclusiveBetween(-1_000_000, 1_000_000);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(InventoryItem.ReasonMaxLength);
    }
}

public class AdjustStockHandler : IRequestHandler<AdjustStockCommand, Result<StockLevelDto>>
{
    private readonly IInventoryRepository _inventory;
    private readonly IStockMovementRepository _movements;
    private readonly InventoryWriter _writer;

    public AdjustStockHandler(IInventoryRepository inventory, IStockMovementRepository movements, InventoryWriter writer)
    {
        _inventory = inventory; _movements = movements; _writer = writer;
    }

    public async Task<Result<StockLevelDto>> Handle(AdjustStockCommand cmd, CancellationToken ct)
    {
        InventoryItem? item = null;
        await _writer.SaveAsync(async () =>
        {
            item = await _inventory.GetForProductAsync(cmd.ProductId, ct);
            if (item is not null) await _movements.AddAsync(item.Adjust(cmd.Delta, cmd.Reason), ct);
        }, ct);

        return item is null
            ? Result<StockLevelDto>.Failure(Error.NotFound("المنتج غير موجود"))
            : Result<StockLevelDto>.Success(StockLevelDto.From(item));
    }
}

// حدّ تنبيه المخزون المنخفض لمنتج (inventory.manage، مُدقَّق) — من وحدة المخزون لا من نموذج المنتج.
public record SetLowStockThresholdCommand(int ProductId, int Threshold) : IRequest<Result<StockLevelDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("inventory.threshold-changed", "Product", ProductId.ToString(),
        Metadata: new Dictionary<string, object?> { ["threshold"] = Threshold });
}

public sealed class SetLowStockThresholdValidator : AbstractValidator<SetLowStockThresholdCommand>
{
    public SetLowStockThresholdValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Threshold).InclusiveBetween(0, 1_000_000);
    }
}

public class SetLowStockThresholdHandler : IRequestHandler<SetLowStockThresholdCommand, Result<StockLevelDto>>
{
    private readonly IInventoryRepository _inventory;
    private readonly InventoryWriter _writer;

    public SetLowStockThresholdHandler(IInventoryRepository inventory, InventoryWriter writer)
    {
        _inventory = inventory; _writer = writer;
    }

    public async Task<Result<StockLevelDto>> Handle(SetLowStockThresholdCommand cmd, CancellationToken ct)
    {
        InventoryItem? item = null;
        await _writer.SaveAsync(async () =>
        {
            item = await _inventory.GetForProductAsync(cmd.ProductId, ct);
            item?.SetLowStockThreshold(cmd.Threshold);
        }, ct);

        return item is null
            ? Result<StockLevelDto>.Failure(Error.NotFound("المنتج غير موجود"))
            : Result<StockLevelDto>.Success(StockLevelDto.From(item));
    }
}
