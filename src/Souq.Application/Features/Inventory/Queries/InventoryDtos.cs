namespace Souq.Application.Features.Inventory.Queries;

// ============================================================================
// عقود الجرد (DTOs) الموجّهة للإدارة فقط. منفصلة عن ProductDto العام كي لا يتسرّب
// حدّ التنبيه (LowStockThreshold) وحالة النشاط لعقد المنتج الذي يراه العميل.
// ============================================================================

// سطر في شاشة جرد المخزون: مخزون المنتج الحالي + حدّ تنبيهه + هل هو منخفض.
public record InventoryItemDto(
    int Id, string NameAr, string NameEn, string ImageUrl, string? CategoryName,
    int StockQuantity, int LowStockThreshold, bool IsLowStock);

// سطر في سجلّ حركة مخزون منتج. Type يُسلسَل نصاً ("Sale"/"Purchase"...).
public record StockMovementDto(
    int Id, string Type, int QuantityChange, int NewQuantity, string? Note, System.DateTime CreatedAt);
