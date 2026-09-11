using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Inventory.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ IInventoryQueries (ADR-0008) — كل القوائم مرقّمة ومُسقطة بلا تتبّع. "منخفض المخزون" مقارنة عمودين (تترجم
// لـ SQL) لا الخاصية المحسوبة Product.IsLowStock؛ القاعدة نفسها: المخزون ≤ حدّ التنبيه. الجرد يشمل المسودّة
// والنشط (المخزون حقيقي في الحالتين) لا المؤرشف.
// ============================================================================
internal sealed class InventoryQueries : IInventoryQueries
{
    private readonly AppDbContext _db;
    public InventoryQueries(AppDbContext db) => _db = db;

    private static Expression<Func<Product, InventoryItemDto>> ToItemDto(string culture) => p => new InventoryItemDto(
        p.Id,
        p.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
            ?? p.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault() ?? p.Slug,
        p.Variants.Where(v => v.IsDefault).Select(v => v.Sku).FirstOrDefault(),
        p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).FirstOrDefault(),
        p.Category!.Translations.Where(t => t.Culture == culture).Select(t => t.Name).FirstOrDefault()
            ?? p.Category.Translations.OrderBy(t => t.Culture).Select(t => t.Name).FirstOrDefault(),
        p.StockQuantity, p.LowStockThreshold, p.StockQuantity <= p.LowStockThreshold);

    public Task<PaginatedList<InventoryItemDto>> ListAsync(PageRequest page, string culture, CancellationToken ct) =>
        CriticalFirst(StockedProducts()).ToPageAsync(ToItemDto(culture), page, ct);

    public Task<PaginatedList<InventoryItemDto>> ListLowStockAsync(PageRequest page, string culture, CancellationToken ct) =>
        CriticalFirst(StockedProducts().Where(p => p.StockQuantity <= p.LowStockThreshold)).ToPageAsync(ToItemDto(culture), page, ct);

    // الأحدث أولاً؛ المعرّف يكسر تعادل حركتين في اللحظة نفسها (نفس معاملة الحفظ).
    public async Task<PaginatedList<StockMovementDto>> ListMovementsAsync(int productId, PageRequest page, CancellationToken ct)
    {
        var rows = await _db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .ToPageAsync(m => new MovementRow(m.Id, m.Type, m.QuantityChange, m.NewQuantity, m.Note, m.CreatedAt), page, ct);

        return rows.Map(r => new StockMovementDto(r.Id, r.Type.ToString(), r.QuantityChange, r.NewQuantity, r.Note, r.CreatedAt));
    }

    private IQueryable<Product> StockedProducts() =>
        _db.Products.AsNoTracking().Where(p => p.Status != ProductStatus.Archived);

    // الأقلّ مخزوناً أولاً (الحرج في الأعلى)، والمعرّف كاسر تعادل.
    private static IOrderedQueryable<Product> CriticalFirst(IQueryable<Product> products) =>
        products.OrderBy(p => p.StockQuantity).ThenBy(p => p.Id);

    private sealed record MovementRow(
        int Id, StockMovementType Type, int QuantityChange, int NewQuantity, string? Note, DateTime CreatedAt);
}
