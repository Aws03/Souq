namespace Souq.Application.Features.Inventory.Queries;

// ============================================================================
// عقود الجرد (DTOs) الموجّهة للإدارة فقط. منفصلة عن ProductDto العام كي لا يتسرّب الموجود والمحجوز وحدّ التنبيه لعقد
// المنتج الذي يراه العميل. الاسم بلغة المتجر الافتراضية، وSKU للبحث البصري في المستودع.
// ============================================================================

// سطر في شاشة الجرد (المرحلة 6): صفّ لكل متغيّر. Id معرّف المنتج (مسارات الإدارة بالمنتج صالحة لمنتج بمتغيّر واحد)،
// VariantId مفتاح المسارات بالمتغيّر، وSKU للمتغيّر نفسه. الموجود، المحجوز لطلبات لم تُدفع، المتاح للبيع (الفرق)، وحدّ
// التنبيه — والانخفاض يُقاس على المتاح. VariantLabel وصف المتغيّر بلغة المتجر ("M / أحمر"؛ null لمنتج بلا خيارات)، وVariantIsActive
// لأن مخزون المتغيّر المعطّل حقيقي ويُعرض، لكنه لا يُباع (ADR-0040).
public record InventoryItemDto(
    int Id, string Name, string? Sku, string? ImageUrl, string? CategoryName,
    int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLowStock, int VariantId,
    string? VariantLabel, bool VariantIsActive);

// سطر في سجلّ حركة مخزون. Type يُسلسَل نصاً ("Sale"/"Purchase"...). NewQuantity = موجود المتغيّر بعد الحركة، وVariantId
// متغيّرها (سجلّ المنتج يجمع متغيّراته).
public record StockMovementDto(
    int Id, string Type, int QuantityChange, int NewQuantity, string? Note, System.DateTime CreatedAt, int VariantId);
