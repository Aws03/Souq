using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Features.Inventory.Reservations;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Inventory.Commands;

// مستوى مخزون متغيّر بعد عملية إدارة — تعيده الأوامر كي تحدّث الواجهة سطرها بلا طلب جديد.
public record StockLevelDto(
    int ProductId, int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLowStock, int VariantId)
{
    internal static StockLevelDto From(InventoryItem item) =>
        new(item.ProductId, item.OnHand, item.Reserved, item.Available, item.LowStockThreshold, item.IsLowStock, item.VariantId);
}

// ============================================================================
// الوصول لمخزون تعدّله الإدارة: بالمتغيّر (الصفّ نفسه)، أو بالمنتج — العقد الأصلي، صالح ما دام للمنتج متغيّر واحد. منتج
// بأكثر من متغيّر يُرفض بـ VariantRequired بدل تعديل مخزون متغيّر لم يقصده المدير. من وحدة Inventory وحدها: عدد صفوف
// المخزون للمنتج، لا الكتالوج. القواعد نفسها في InventoryItem للمسارين.
// ============================================================================
internal static class StockTarget
{
    public static Func<IInventoryRepository, CancellationToken, Task<Result<InventoryItem>>> Product(int productId) =>
        async (inventory, ct) => await inventory.ListForProductAsync(productId, ct) switch
        {
            { Count: 0 } => Result<InventoryItem>.Failure(Error.NotFound("المنتج غير موجود")),
            { Count: 1 } items => Result<InventoryItem>.Success(items[0]),
            _ => Result<InventoryItem>.Failure(Error.BusinessRule("VariantRequired",
                "للمنتج أكثر من متغيّر: عدّل مخزون المتغيّر المطلوب")),
        };

    public static Func<IInventoryRepository, CancellationToken, Task<Result<InventoryItem>>> Variant(int variantId) =>
        async (inventory, ct) => await inventory.GetForVariantAsync(variantId, ct) is { } item
            ? Result<InventoryItem>.Success(item)
            : Result<InventoryItem>.Failure(Error.NotFound("المتغيّر غير موجود"));

    // التحميل داخل محاولة الحفظ (InventoryWriter يعيدها بعد تعارض تزامن من قراءة جديدة).
    public static async Task<Result<StockLevelDto>> ChangeAsync(
        InventoryWriter writer, IInventoryRepository inventory,
        Func<IInventoryRepository, CancellationToken, Task<Result<InventoryItem>>> find,
        Func<InventoryItem, Task> change, CancellationToken ct)
    {
        Result<InventoryItem>? found = null;
        await writer.SaveAsync(async () =>
        {
            found = await find(inventory, ct);
            if (found.IsSuccess) await change(found.Value!);
        }, ct);

        return found!.IsSuccess
            ? Result<StockLevelDto>.Success(StockLevelDto.From(found.Value!))
            : Result<StockLevelDto>.Failure(found.Error!);
    }
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

    public Task<Result<StockLevelDto>> Handle(AdjustStockCommand cmd, CancellationToken ct) =>
        StockTarget.ChangeAsync(_writer, _inventory, StockTarget.Product(cmd.ProductId),
            item => _movements.AddAsync(item.Adjust(cmd.Delta, cmd.Reason), ct), ct);
}

// تصحيح مخزون متغيّر بعينه — القواعد والتدقيق كتصحيح المنتج، والمرجع المتغيّر.
public record AdjustVariantStockCommand(int VariantId, int Delta, string Reason) : IRequest<Result<StockLevelDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("inventory.adjusted", "ProductVariant", VariantId.ToString(),
        Metadata: new Dictionary<string, object?> { ["delta"] = Delta, ["reason"] = Reason });
}

public sealed class AdjustVariantStockValidator : AbstractValidator<AdjustVariantStockCommand>
{
    public AdjustVariantStockValidator()
    {
        RuleFor(x => x.VariantId).GreaterThan(0);
        RuleFor(x => x.Delta).NotEqual(0).InclusiveBetween(-1_000_000, 1_000_000);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(InventoryItem.ReasonMaxLength);
    }
}

public class AdjustVariantStockHandler : IRequestHandler<AdjustVariantStockCommand, Result<StockLevelDto>>
{
    private readonly IInventoryRepository _inventory;
    private readonly IStockMovementRepository _movements;
    private readonly InventoryWriter _writer;

    public AdjustVariantStockHandler(IInventoryRepository inventory, IStockMovementRepository movements, InventoryWriter writer)
    {
        _inventory = inventory; _movements = movements; _writer = writer;
    }

    public Task<Result<StockLevelDto>> Handle(AdjustVariantStockCommand cmd, CancellationToken ct) =>
        StockTarget.ChangeAsync(_writer, _inventory, StockTarget.Variant(cmd.VariantId),
            item => _movements.AddAsync(item.Adjust(cmd.Delta, cmd.Reason), ct), ct);
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

    public Task<Result<StockLevelDto>> Handle(SetLowStockThresholdCommand cmd, CancellationToken ct) =>
        StockTarget.ChangeAsync(_writer, _inventory, StockTarget.Product(cmd.ProductId), item =>
        {
            item.SetLowStockThreshold(cmd.Threshold);
            return Task.CompletedTask;
        }, ct);
}

public record SetVariantLowStockThresholdCommand(int VariantId, int Threshold) : IRequest<Result<StockLevelDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("inventory.threshold-changed", "ProductVariant", VariantId.ToString(),
        Metadata: new Dictionary<string, object?> { ["threshold"] = Threshold });
}

public sealed class SetVariantLowStockThresholdValidator : AbstractValidator<SetVariantLowStockThresholdCommand>
{
    public SetVariantLowStockThresholdValidator()
    {
        RuleFor(x => x.VariantId).GreaterThan(0);
        RuleFor(x => x.Threshold).InclusiveBetween(0, 1_000_000);
    }
}

public class SetVariantLowStockThresholdHandler : IRequestHandler<SetVariantLowStockThresholdCommand, Result<StockLevelDto>>
{
    private readonly IInventoryRepository _inventory;
    private readonly InventoryWriter _writer;

    public SetVariantLowStockThresholdHandler(IInventoryRepository inventory, InventoryWriter writer)
    {
        _inventory = inventory; _writer = writer;
    }

    public Task<Result<StockLevelDto>> Handle(SetVariantLowStockThresholdCommand cmd, CancellationToken ct) =>
        StockTarget.ChangeAsync(_writer, _inventory, StockTarget.Variant(cmd.VariantId), item =>
        {
            item.SetLowStockThreshold(cmd.Threshold);
            return Task.CompletedTask;
        }, ct);
}
