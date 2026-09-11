using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Inventory.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ IInventoryQueries (ADR-0008) — كل القوائم مرقّمة ومُسقطة بلا تتبّع. المرحلة 6: الأرقام من InventoryItems (مخزون
// المتغيّر الافتراضي لكل منتج)، و"منخفض" مقارنة المتاح (الموجود − المحجوز) بحدّ التنبيه في SQL. الجرد يشمل المسودّة
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

    // الأحدث أولاً؛ المعرّف يكسر تعادل حركتين في اللحظة نفسها (نفس معاملة الحفظ).
    public async Task<PaginatedList<StockMovementDto>> ListMovementsAsync(int productId, PageRequest page, CancellationToken ct)
    {
        var rows = await _db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .ToPageAsync(m => new MovementRow(m.Id, m.Type, m.QuantityChange, m.NewQuantity, m.Note, m.CreatedAt), page, ct);

        return rows.Map(r => new StockMovementDto(r.Id, r.Type.ToString(), r.QuantityChange, r.NewQuantity, r.Note, r.CreatedAt));
    }

    // مخزون المتغيّر الافتراضي لكل منتج غير مؤرشف.
    private IQueryable<InventoryItem> StockedItems() =>
        _db.InventoryItems.AsNoTracking().Where(i =>
            _db.Set<ProductVariant>().Any(v => v.Id == i.VariantId && v.IsDefault)
            && _db.Products.Any(p => p.Id == i.ProductId && p.Status != ProductStatus.Archived));

    // الأقلّ متاحاً أولاً (الحرج في الأعلى)، والمنتج كاسر تعادل.
    private Task<PaginatedList<InventoryItemDto>> PageAsync(
        IQueryable<InventoryItem> items, PageRequest page, string culture, CancellationToken ct) =>
        items.Join(_db.Products, i => i.ProductId, p => p.Id, (i, p) => new { Item = i, Product = p })
            .OrderBy(x => x.Item.OnHand - x.Item.Reserved).ThenBy(x => x.Product.Id)
            .ToPageAsync(x => new InventoryItemDto(
                x.Product.Id,
                x.Product.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                    ?? x.Product.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault() ?? x.Product.Slug,
                x.Product.Variants.Where(v => v.IsDefault).Select(v => v.Sku).FirstOrDefault(),
                x.Product.Images.OrderBy(im => im.SortOrder).ThenBy(im => im.Id).Select(im => im.Url).FirstOrDefault(),
                x.Product.Category!.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
                    ?? x.Product.Category.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
                x.Item.OnHand, x.Item.Reserved, x.Item.OnHand - x.Item.Reserved, x.Item.LowStockThreshold,
                x.Item.OnHand - x.Item.Reserved <= x.Item.LowStockThreshold), page, ct);

    private sealed record MovementRow(
        int Id, StockMovementType Type, int QuantityChange, int NewQuantity, string? Note, DateTime CreatedAt);
}
