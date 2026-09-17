using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للمنتجات: ما تحتاجه الأوامر فقط. قراءات العرض في ICatalogQueries/IInventoryQueries بإسقاط مباشر
// (ADR-0008). GetByIdAsync يحمّل التجمّع كاملاً (ترجمات، صور، متغيّرات) — كل قاعدة تحتاج أبناءه.
public interface IProductRepository : IRepository<Product>
{
    // عدّة منتجات بتجمّعاتها في دفعة واحدة (خطّ التسعير، المرحلة 8) — لا استعلام لكل سطر. الغائب لا يُعاد.
    Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

    // هل يشير أي منتج (بأي حالة) لهذه الفئة؟ لمنع حذف فئة مستخدمة — المفتاح الأجنبي قائم بغضّ النظر عن الحالة.
    Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default);

    // تفرّد معرّف الرابط وSKU داخل المتجر (فهرسان فريدان في القاعدة هما الحارس الأخير) — exceptProductId للتعديل.
    Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct = default);

    Task<bool> SkuExistsAsync(string sku, int? exceptProductId, CancellationToken ct = default);

    // تعديل الخيارات والمتغيّرات يكتب صفوف الأبناء وحدها فلا يمرّ بـ rowversion الجذر — ومديران يضيفان خياراً ومتغيّراً معاً
    // قد يتركان متغيّراً بلا قيمة للخيار الجديد. هذا يجعل الحفظ التالي يحدّث صف المنتج نفسه، فيُرفض أحدهما بتعارض (409).
    void GuardConcurrentEdit(Product product);
}
