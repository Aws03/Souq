using Souq.Application.Common.Models;
using Souq.Application.Features.Categories.Queries;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// ICatalogQueries — منفذ القراءة لوحدة Catalog (ADR-0008). التنفيذ يُسقط من SQL إلى DTO مباشرة (AsNoTracking +
// Select). culture: لغة المتجر الافتراضية — منها Name/Description في العقد، والترجمات كلها معه. المتجر: المنتجات
// النشطة في فئات مفعّلة فقط؛ الإدارة: كل الحالات. مرشّح المستأجر يعمل على كل استعلام تلقائياً.
// ============================================================================
public interface ICatalogQueries
{
    Task<ProductSearchPage> SearchProductsAsync(ProductSearch search, PageRequest page, string culture, CancellationToken ct);

    // اقتراحات أثناء الكتابة (M3): منتجات معروضة ثم فئات مفعَّلة، بالبادئة أولاً ثم الاحتواء. كلمة أقصر من
    // SearchSuggestionRules.MinKeywordLength تعود فارغة بلا استعلام.
    Task<IReadOnlyList<SearchSuggestionDto>> SuggestAsync(string? keyword, int limit, string culture, CancellationToken ct);

    // null ⇒ غير موجود أو غير معروض (مسودّة/مؤرشف/فئته معطّلة) — العميل لا يرى إلا المعروض.
    Task<ProductDto?> FindActiveProductAsync(int id, string culture, CancellationToken ct);

    Task<ProductDto?> FindActiveProductBySlugAsync(string slug, string culture, CancellationToken ct);

    // null ⇒ المنتج نفسه غير معروض. نفس الفئة أولاً (الأكثر مبيعاً) ثم أحدث غيرها.
    Task<IReadOnlyList<ProductDto>?> FindRelatedProductsAsync(int productId, int count, string culture, CancellationToken ct);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(bool includeInactive, string culture, CancellationToken ct);

    Task<PaginatedList<AdminProductListItemDto>> ListAdminProductsAsync(
        AdminProductSearch search, PageRequest page, string culture, CancellationToken ct);

    Task<AdminProductDto?> FindAdminProductAsync(int id, CancellationToken ct);

    // مفردات البحث لشاشة التاجر (M3): الجدول صغير بحدّه، فقائمة كاملة بلا ترقيم.
    Task<IReadOnlyList<SearchSynonymDto>> ListSearchSynonymsAsync(CancellationToken ct);
}

// معايير بحث المتجر — كلها اختيارية؛ null/فارغ = بلا تصفية على هذا البعد. OnSaleOnly: سعر مقارنة أعلى من السعر.
// ExactOnly (M3، ADR-0042): المتسوّق أصرّ على كلماته بعد أن عُرض عليه تصحيح — فلا تصحيح، ولو كانت النتيجة فارغة.
// إصراره قرارُه: هذا هو النصف الثاني من "لا استبدال صامت"، إذ لا معنى لإخباره بالتصحيح إن لم يستطع رفضه.
public sealed record ProductSearch(
    string? Keyword = null,
    IReadOnlyCollection<int>? CategoryIds = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    ProductSortBy SortBy = ProductSortBy.Newest,
    bool OnSaleOnly = false,
    bool ExactOnly = false);

// بحث الإدارة: الاسم بأي لغة أو SKU أو المعرّف، وتصفية بالحالة والفئة.
public sealed record AdminProductSearch(string? Keyword, ProductStatus? Status, int? CategoryId, AdminProductSortBy SortBy);

public enum AdminProductSortBy
{
    Newest = 0,
    NameAsc = 1,
    PriceAsc = 2,
    PriceDesc = 3,
    StockAsc = 4,
}
