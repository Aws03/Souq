using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// ============================================================================
// منفذ الكتابة لوحدة Inventory (المرحلة 6): المخزون وحجوزاته. القراءات للعرض في IInventoryQueries (ADR-0008).
// الحركات تُضاف عبر IStockMovementRepository — والكيان وحده يُنشئها.
// ============================================================================
public interface IInventoryRepository
{
    Task AddAsync(InventoryItem item, CancellationToken ct = default);

    // مخزون متغيّرات منتج (صفّ لكل متغيّر). مسارات الإدارة بالمنتج تقبل منتجاً بصفّ واحد فقط.
    Task<IReadOnlyList<InventoryItem>> ListForProductAsync(int productId, CancellationToken ct = default);

    Task<InventoryItem?> GetForVariantAsync(int variantId, CancellationToken ct = default);

    Task<IReadOnlyList<InventoryItem>> GetByVariantsAsync(IReadOnlyCollection<int> variantIds, CancellationToken ct = default);

    Task<IReadOnlyList<InventoryItem>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    Task AddReservationAsync(StockReservation reservation, CancellationToken ct = default);

    // كل حجوزات مرجع (بأي حالة) — العمليات تقرّر بحسب الحالة، فالتكرار آمن.
    Task<IReadOnlyList<StockReservation>> GetReservationsAsync(string reference, CancellationToken ct = default);

    // مراجع لها حجز نشط انتهت مهلته — لمنسّق انتهاء الطلبات المعلّقة (الأقدم أولاً، بحدّ أقصى).
    Task<IReadOnlyList<string>> FindExpiredReferencesAsync(DateTime now, int max, CancellationToken ct = default);

    // بعد تعارض تزامن: ينسى ما حُمِّل من مخزون وحجوزات وحركات غير محفوظة كي تقرأ المحاولة التالية من القاعدة.
    void Reset();
}
