using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Queries;

// معالج الاستعلام: يترجم الطلب إلى معايير بحث مطبوعة ويمرّرها لمنفذ القراءة. لا يعرف SQL
// ولا EF — الإسقاط والترتيب الحتمي في CatalogQueries (Infrastructure، ADR-0008).
public class GetProductsHandler : IRequestHandler<GetProductsQuery, PaginatedList<ProductDto>>
{
    private readonly ICatalogQueries _catalog;
    public GetProductsHandler(ICatalogQueries catalog) => _catalog = catalog;

    public Task<PaginatedList<ProductDto>> Handle(GetProductsQuery q, CancellationToken ct) =>
        _catalog.SearchProductsAsync(
            new ProductSearch(q.Keyword, q.CategoryIds, q.MinPrice, q.MaxPrice, q.SortBy),
            PageRequest.From(q), ct);
}
