using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Inventory.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ IInventoryQueries (ADR-0008) — كل القوائم مرقّمة ومُسقطة بلا تتبّع. المرحلة 6: الأرقام من InventoryItems (صفّ لكل
// متغيّر)، و"منخفض" مقارنة المتاح (الموجود − المحجوز) بحدّ التنبيه في SQL. الجرد يشمل المسودّة
// والنشط (المخزون حقيقي في الحالتين) لا المؤرشف.
// ============================================================================
internal sealed class InventoryQueries : IInventoryQueries
{
    private readonly AppDbContext _db;
    public InventoryQueries(AppDbContext db) => _db = db;

    public Task<PaginatedList<InventoryItemDto>> ListAsync(PageRequest page, string culture, CancellationToken ct) =>
        PageAsync(StockedItems(), page, culture, ct);

    public Task<PaginatedList<InventoryItemDto>> ListLowStockAsync(PageRequest page, string culture, CancellationToken ct) =>
        PageAsync(StockedItems().Where(i => i.OnHand - i.Reserved <= i.LowStockThreshold), page, culture, ct);

    public Task<PaginatedList<StockMovementDto>> ListMovementsAsync(int productId, PageRequest page, CancellationToken ct) =>
        MovementsAsync(_db.StockMovements.Where(m => m.ProductId == productId), page, ct);

    public Task<PaginatedList<StockMovementDto>> ListVariantMovementsAsync(int variantId, PageRequest page, CancellationToken ct) =>
        MovementsAsync(
            _db.StockMovements.Where(m => _db.InventoryItems.Any(i => i.Id == m.InventoryItemId && i.VariantId == variantId)), page, ct);

    // الأحدث أولاً؛ المعرّف يكسر تعادل حركتين في اللحظة نفسها (نفس معاملة الحفظ).
    private async Task<PaginatedList<StockMovementDto>> MovementsAsync(IQueryable<StockMovement> movements, PageRequest page, CancellationToken ct)
    {
        var rows = await movements.AsNoTracking()
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .ToPageAsync(m => new MovementRow(m.Id, m.Type, m.QuantityChange, m.NewQuantity, m.Note, m.CreatedAt,
                _db.InventoryItems.Where(i => i.Id == m.InventoryItemId).Select(i => i.VariantId).FirstOrDefault()), page, ct);

        return rows.Map(r => new StockMovementDto(r.Id, r.Type.ToString(), r.QuantityChange, r.NewQuantity, r.Note, r.CreatedAt, r.VariantId));
    }

    // صفّ لكل متغيّر من منتج غير مؤرشف (المعطّل منها أيضاً: مخزونه حقيقي).
    private IQueryable<InventoryItem> StockedItems() =>
        _db.InventoryItems.AsNoTracking().Where(i =>
            _db.Products.Any(p => p.Id == i.ProductId && p.Status != ProductStatus.Archived));

    // الأقلّ متاحاً أولاً (الحرج في الأعلى)، والمنتج كاسر تعادل. وصف المتغيّر: أسماء قيمه بترتيب الخيارات، يُركَّب بقاعدة Catalog
    // الواحدة (VariantLabels) بعد القراءة — القراءة هنا من جداول الكتالوج كما الاسم وSKU، والجرد لا يملك منها شيئاً.
    private async Task<PaginatedList<InventoryItemDto>> PageAsync(
        IQueryable<InventoryItem> items, PageRequest page, string culture, CancellationToken ct)
    {
        var rows = await items.Join(_db.Products, i => i.ProductId, p => p.Id, (i, p) => new { Item = i, Product = p })
            .OrderBy(x => x.Item.OnHand - x.Item.Reserved).ThenBy(x => x.Product.Id).ThenBy(x => x.Item.VariantId)
            .ToPageAsync(x => new ItemRow(
                x.Product.Id,
                x.Product.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                    ?? x.Product.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault() ?? x.Product.Slug,
                x.Product.Variants.Where(v => v.Id == x.Item.VariantId).Select(v => v.Sku).FirstOrDefault(),
                x.Product.Images.OrderBy(im => im.SortOrder).ThenBy(im => im.Id).Select(im => im.Url).FirstOrDefault(),
                x.Product.Category!.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                    ?? x.Product.Category.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
                x.Item.OnHand, x.Item.Reserved, x.Item.LowStockThreshold, x.Item.VariantId,
                x.Product.Variants.Where(v => v.Id == x.Item.VariantId).Select(v => v.IsActive).FirstOrDefault(),
                x.Product.Variants.Where(v => v.Id == x.Item.VariantId).SelectMany(v => v.OptionValues)
                    .Select(ov => new ValueRow(
                        _db.Set<ProductOption>()
                            .Where(o => o.Id == EF.Property<int>(ov.OptionValue, "ProductOptionId"))
                            .Select(o => o.Position).FirstOrDefault(),
                        ov.OptionValue.Translations.Select(t => new NameRow(t.Culture, t.Name)).ToList()))
                    .ToList()), page, ct);

        return rows.Map(r => new InventoryItemDto(
            r.ProductId, r.Name, r.Sku, r.ImageUrl, r.CategoryName, r.OnHand, r.Reserved, r.OnHand - r.Reserved, r.LowStockThreshold,
            r.OnHand - r.Reserved <= r.LowStockThreshold, r.VariantId,
            VariantLabels.Compose(r.Values.OrderBy(v => v.OptionPosition)
                .Select(v => (IReadOnlyDictionary<string, string>)v.Names.ToDictionary(n => n.Culture, n => n.Name)), culture),
            r.VariantIsActive));
    }

    private sealed record ItemRow(
        int ProductId, string Name, string? Sku, string? ImageUrl, string? CategoryName, int OnHand, int Reserved, int LowStockThreshold,
        int VariantId, bool VariantIsActive, List<ValueRow> Values);

    private sealed record ValueRow(int OptionPosition, List<NameRow> Names);

    private sealed record NameRow(string Culture, string Name);

    private sealed record MovementRow(
        int Id, StockMovementType Type, int QuantityChange, int NewQuantity, string? Note, DateTime CreatedAt, int VariantId);
}
