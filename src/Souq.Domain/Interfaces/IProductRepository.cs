using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للمنتجات: ما تحتاجه الأوامر فقط. قراءات العرض في ICatalogQueries/IInventoryQueries بإسقاط مباشر
// (ADR-0008). GetByIdAsync يحمّل التجمّع كاملاً (ترجمات، صور، متغيّرات) — كل قاعدة تحتاج أبناءه.
public interface IProductRepository : IRepository<Product>
{
    // هل يشير أي منتج (بأي حالة) لهذه الفئة؟ لمنع حذف فئة مستخدمة — المفتاح الأجنبي قائم بغضّ النظر عن الحالة.
    Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default);

    // تفرّد معرّف الرابط وSKU داخل المتجر (فهرسان فريدان في القاعدة هما الحارس الأخير) — exceptProductId للتعديل.
    Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct = default);

    Task<bool> SkuExistsAsync(string sku, int? exceptProductId, CancellationToken ct = default);
}
