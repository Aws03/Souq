namespace Souq.Application.Features.Inventory.Queries;

// ============================================================================
// عقود الجرد (DTOs) الموجّهة للإدارة فقط. منفصلة عن ProductDto العام كي لا يتسرّب الموجود والمحجوز وحدّ التنبيه لعقد
// المنتج الذي يراه العميل. الاسم بلغة المتجر الافتراضية، وSKU للبحث البصري في المستودع.
// ============================================================================

// سطر في شاشة الجرد (المرحلة 6): Id معرّف المنتج (مسارات الإدارة بالمنتج — متغيّر افتراضي واحد)، الموجود، المحجوز
// لطلبات لم تُدفع، المتاح للبيع (الفرق)، وحدّ التنبيه — والانخفاض يُقاس على المتاح.
public record InventoryItemDto(
    int Id, string Name, string? Sku, string? ImageUrl, string? CategoryName,
    int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLowStock);

// سطر في سجلّ حركة مخزون منتج. Type يُسلسَل نصاً ("Sale"/"Purchase"...). NewQuantity = الموجود بعد الحركة.
public record StockMovementDto(
    int Id, string Type, int QuantityChange, int NewQuantity, string? Note, System.DateTime CreatedAt);
