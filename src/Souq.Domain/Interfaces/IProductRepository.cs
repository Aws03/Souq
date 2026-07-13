using Souq.Domain.Entities;
using Souq.Domain.Enums;

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

    // بحث الكتالوج: كلمة + فئات متعددة (فارغة/null = الكل) + نطاق سعر + ترتيب.
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? keyword, IReadOnlyCollection<int>? categoryIds, int page, int pageSize,
        decimal? minPrice = null, decimal? maxPrice = null,
        ProductSortBy sortBy = ProductSortBy.Newest, CancellationToken ct = default);

    // هل يشير أي منتج (نشط أو معطّل) لهذه الفئة؟ (لمنع حذف فئة مستخدمة —
    // نشمل المعطّلة لأن المفتاح الأجنبي قائم بغضّ النظر عن IsActive).
    Task<bool> ExistsInCategoryAsync(int categoryId, CancellationToken ct = default);

    // كل المنتجات النشطة مع فئاتها (لشاشة جرد المخزون) — الأقلّ مخزوناً أولاً كي
    // يرى المدير المنتجات الحرجة في الأعلى مباشرة.
    Task<IReadOnlyList<Product>> GetInventoryAsync(CancellationToken ct = default);

    // المنتجات النشطة التي بلغ مخزونها حدّ التنبيه أو نزل تحته — للتنبيه والبادج.
    Task<IReadOnlyList<Product>> GetLowStockAsync(CancellationToken ct = default);
}
