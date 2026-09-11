using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة للمنتجات: ما تحتاجه الأوامر فقط. قراءات العرض (البحث، التفاصيل، ذات الصلة،
// الجرد) انتقلت إلى ICatalogQueries/IInventoryQueries بإسقاط مباشر (ADR-0008, Phase 0 D3) —
// كان هذا المستودع يحمل ثماني طرق قراءة وتوقيع بحث بثمانية معاملات.
public interface IProductRepository : IRepository<Product>
{
    // هل يشير أي منتج (نشط أو معطّل) لهذه الفئة؟ لمنع حذف فئة مستخدمة — نشمل المعطّلة
    // لأن المفتاح الأجنبي قائم بغضّ النظر عن IsActive.
    Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default);
}
