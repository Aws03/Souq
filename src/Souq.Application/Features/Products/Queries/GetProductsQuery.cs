using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// نمط CQRS: هذا "استعلام" (Query) — يقرأ ولا يُعدّل شيئاً.
// IRequest<...> من MediatR: نُعرّف الطلب ككائن بيانات، ويتولّى MediatR توجيهه
// تلقائياً إلى معالجه (Handler) المناسب. الفائدة: فصل تام بين "ما نريد فعله"
// و"كيف يُفعل"، وكل عملية في ملف معزول قابل للاختبار وحده.
// ============================================================================
// CategoryIds فارغة أو null = كل الفئات (تعدُّد فئات بدل فئة واحدة سابقاً).
public record GetProductsQuery(
    string? Keyword = null,
    List<int>? CategoryIds = null,
    int Page = 1,
    int PageSize = 12,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    ProductSortBy SortBy = ProductSortBy.Newest
) : IRequest<PaginatedList<ProductDto>>;
