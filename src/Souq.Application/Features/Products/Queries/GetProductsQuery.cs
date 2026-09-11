using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// نمط CQRS: هذا "استعلام" (Query) — يقرأ ولا يُعدّل شيئاً.
// IRequest<...> من MediatR: نُعرّف الطلب ككائن بيانات، ويتولّى MediatR توجيهه
// تلقائياً إلى معالجه (Handler) المناسب. IPagedQuery: قواعد الترقيم الموحّدة تنطبق عليه.
// ============================================================================
// CategoryIds فارغة أو null = كل الفئات.
public record GetProductsQuery(
    string? Keyword = null,
    List<int>? CategoryIds = null,
    int Page = 1,
    int PageSize = 12,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    ProductSortBy SortBy = ProductSortBy.Newest
) : IRequest<PaginatedList<ProductDto>>, IPagedQuery;
