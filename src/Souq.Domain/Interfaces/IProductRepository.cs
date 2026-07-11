using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// واجهة متخصّصة للمنتجات: ترث العام وتضيف استعلامات خاصة بالمنتجات.
// نضيف فقط ما يحتاجه المجال فعلاً (لا نخمّن المستقبل).
public interface IProductRepository : IRepository<Product>
{
    // جلب منتج واحد لعرض العميل: يشمل الفئة (لا CategoryName فارغ) ويستبعد
    // المعطّل (لا يُعرض/يُطلب منتج أُلغي تفعيله). عمليات الأدمن (تعديل/حذف/رفع
    // صورة) تستخدم GetByIdAsync العام لأنها يجب أن تعمل بغضّ النظر عن IsActive.
    Task<Product?> GetActiveByIdAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId, CancellationToken ct = default);
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? keyword, int? categoryId, int page, int pageSize, CancellationToken ct = default);

    // هل يشير أي منتج (نشط أو معطّل) لهذه الفئة؟ (لمنع حذف فئة مستخدمة —
    // نشمل المعطّلة لأن المفتاح الأجنبي قائم بغضّ النظر عن IsActive).
    Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default);
}
