using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Inventory.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ IInventoryQueries (ADR-0008) — كل القوائم مرقّمة ومُسقطة بلا تتبّع. "منخفض المخزون"
// مكتوب كمقارنة عمودين (يترجم لـ SQL) لا الخاصية المحسوبة Product.IsLowStock (لا عمود لها)؛
// القاعدة نفسها: المخزون ≤ حدّ التنبيه.
// ============================================================================
internal sealed class InventoryQueries : IInventoryQueries
{
    private readonly AppDbContext _db;
    public InventoryQueries(AppDbContext db) => _db = db;

    private static readonly Expression<Func<Product, InventoryItemDto>> ToItemDto = p => new InventoryItemDto(
        p.Id, p.NameAr, p.NameEn, p.ImageUrl, p.Category!.Name,
        p.StockQuantity, p.LowStockThreshold, p.StockQuantity <= p.LowStockThreshold);

    public Task<PaginatedList<InventoryItemDto>> ListAsync(PageRequest page, CancellationToken ct) =>
        CriticalFirst(ActiveProducts()).ToPageAsync(ToItemDto, page, ct);

    public Task<PaginatedList<InventoryItemDto>> ListLowStockAsync(PageRequest page, CancellationToken ct) =>
        CriticalFirst(ActiveProducts().Where(p => p.StockQuantity <= p.LowStockThreshold)).ToPageAsync(ToItemDto, page, ct);

    // الأحدث أولاً؛ المعرّف يكسر تعادل حركتين في اللحظة نفسها (نفس معاملة الحفظ).
    public async Task<PaginatedList<StockMovementDto>> ListMovementsAsync(int productId, PageRequest page, CancellationToken ct)
    {
        var rows = await _db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .ToPageAsync(m => new MovementRow(m.Id, m.Type, m.QuantityChange, m.NewQuantity, m.Note, m.CreatedAt), page, ct);

        return rows.Map(r => new StockMovementDto(r.Id, r.Type.ToString(), r.QuantityChange, r.NewQuantity, r.Note, r.CreatedAt));
    }

    private IQueryable<Product> ActiveProducts() => _db.Products.AsNoTracking().Where(p => p.IsActive);

    // الأقلّ مخزوناً أولاً (الحرج في الأعلى)، والمعرّف كاسر تعادل.
    private static IOrderedQueryable<Product> CriticalFirst(IQueryable<Product> products) =>
        products.OrderBy(p => p.StockQuantity).ThenBy(p => p.Id);

    private sealed record MovementRow(
        int Id, StockMovementType Type, int QuantityChange, int NewQuantity, string? Note, DateTime CreatedAt);
}
