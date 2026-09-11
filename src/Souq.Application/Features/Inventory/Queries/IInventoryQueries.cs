using Souq.Application.Common.Models;

namespace Souq.Application.Features.Inventory.Queries;

// ============================================================================
// منفذ القراءة لوحدة Inventory (ADR-0008). كل القوائم مرقّمة: الجرد كان يعيد كل المنتجات النشطة وسجلّ الحركة كل
// حركات المنتج منذ إنشائه — سجلّ دفتري ينمو بلا حدّ (Part 15). culture: لغة المتجر الافتراضية لاسم المنتج.
// ============================================================================
public interface IInventoryQueries
{
    // المنتجات النشطة، الأقلّ مخزوناً أولاً (الحرجة في الأعلى).
    Task<PaginatedList<InventoryItemDto>> ListAsync(PageRequest page, string culture, CancellationToken ct);

    // النشطة التي بلغ مخزونها حدّ التنبيه أو نزل تحته. TotalCount يكفي لشارة التنبيه.
    Task<PaginatedList<InventoryItemDto>> ListLowStockAsync(PageRequest page, string culture, CancellationToken ct);

    // حركات مخزون منتج — الأحدث أولاً.
    Task<PaginatedList<StockMovementDto>> ListMovementsAsync(int productId, PageRequest page, CancellationToken ct);
}
