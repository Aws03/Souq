using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مستودع المخزون وحجوزاته (المرحلة 6) — جهة الكتابة فقط؛ القراءات في InventoryQueries. كل استعلام مُرشَّح بالمتجر.
public class InventoryRepository : IInventoryRepository
{
    private readonly AppDbContext _db;
    public InventoryRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(InventoryItem item, CancellationToken ct = default) =>
        await _db.InventoryItems.AddAsync(item, ct);

    public async Task<IReadOnlyList<InventoryItem>> ListForProductAsync(int productId, CancellationToken ct = default) =>
        await _db.InventoryItems.Where(i => i.ProductId == productId).OrderBy(i => i.VariantId).ToListAsync(ct);

    public Task<InventoryItem?> GetForVariantAsync(int variantId, CancellationToken ct = default) =>
        _db.InventoryItems.FirstOrDefaultAsync(i => i.VariantId == variantId, ct);

    public async Task<IReadOnlyList<InventoryItem>> GetByVariantsAsync(IReadOnlyCollection<int> variantIds, CancellationToken ct = default) =>
        await _db.InventoryItems.Where(i => variantIds.Contains(i.VariantId)).ToListAsync(ct);

    public async Task<IReadOnlyList<InventoryItem>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        await _db.InventoryItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);

    public async Task AddReservationAsync(StockReservation reservation, CancellationToken ct = default) =>
        await _db.StockReservations.AddAsync(reservation, ct);

    public async Task<IReadOnlyList<StockReservation>> GetReservationsAsync(string reference, CancellationToken ct = default) =>
        await _db.StockReservations.Where(r => r.Reference == reference).OrderBy(r => r.Id).ToListAsync(ct);

    // الأقدم انتهاءً أولاً: دورة محدودة تعالج الأكثر تأخّراً ولا تجوع أيّ مرجع.
    public async Task<IReadOnlyList<string>> FindExpiredReferencesAsync(DateTime now, int max, CancellationToken ct = default) =>
        await _db.StockReservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .GroupBy(r => r.Reference)
            .OrderBy(g => g.Min(r => r.ExpiresAt))
            .Select(g => g.Key)
            .Take(max)
            .ToListAsync(ct);

    // بعد تعارض تزامن: المحاولة التالية تقرأ القيم الملتزمة. غير المحفوظ (حجوزات وحركات المحاولة الفاشلة) يُنسى أيضاً.
    public void Reset()
    {
        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(e => e.Entity is InventoryItem or StockReservation or StockMovement)
                     .ToList())
            entry.State = EntityState.Detached;
    }
}
