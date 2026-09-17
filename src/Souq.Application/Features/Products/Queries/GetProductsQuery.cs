using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// نمط CQRS: "استعلام" يقرأ ولا يُعدّل. IPagedQuery: قواعد الترقيم الموحّدة تنطبق عليه.
// CategoryIds فارغة أو null = كل الفئات. OnSale: المنتجات بسعر مقارنة أعلى من سعرها (صفحة العروض).
// ============================================================================
public record GetProductsQuery(
    string? Keyword = null,
    List<int>? CategoryIds = null,
    int Page = 1,
    int PageSize = 12,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    ProductSortBy SortBy = ProductSortBy.Newest,
    bool OnSale = false,
    bool Exact = false
) : IRequest<ProductSearchPage>, IPagedQuery;
