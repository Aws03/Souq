using Souq.Application.Common.Models;
using Souq.Application.Features.Categories.Queries;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// ICatalogQueries — منفذ القراءة لوحدة Catalog (ADR-0008). التنفيذ في Infrastructure يُسقط
// من SQL إلى DTO مباشرة (AsNoTracking + Select): لا كيانات كاملة تُحمَّل ثم تُحوَّل في الذاكرة
// (Phase 0 D3)، ولا تعرف Application شيئاً عن EF. التصفية مطبوعة (ProductSearch)، والترتيب
// قائمة مسموحة (ProductSortBy)، والصفحة حتمية دائماً. في المرحلة 2 يعمل مرشّح المستأجر العام
// على هذه الاستعلامات تلقائياً — بلا تغيير في هذا العقد.
// ============================================================================
public interface ICatalogQueries
{
    Task<PaginatedList<ProductDto>> SearchProductsAsync(ProductSearch search, PageRequest page, CancellationToken ct);

    // null ⇒ غير موجود أو معطّل (المنتج المعطّل لا يُعرض للعميل).
    Task<ProductDto?> FindActiveProductAsync(int id, CancellationToken ct);

    // null ⇒ المنتج نفسه غير موجود/معطّل. نفس الفئة أولاً (الأكثر مبيعاً) ثم أحدث غيرها.
    Task<IReadOnlyList<ProductDto>?> FindRelatedProductsAsync(int productId, int count, CancellationToken ct);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct);
}

// معايير البحث — كلها اختيارية؛ null/فارغ = بلا تصفية على هذا البعد.
public sealed record ProductSearch(
    string? Keyword = null,
    IReadOnlyCollection<int>? CategoryIds = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    ProductSortBy SortBy = ProductSortBy.Newest);
