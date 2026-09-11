using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;

namespace Souq.Application.Features.Products.Queries;

// معالج الاستعلام: يترجم الطلب إلى معايير بحث مطبوعة ويمرّرها لمنفذ القراءة مع لغة المتجر الافتراضية. لا يعرف
// SQL ولا EF — الإسقاط والترتيب الحتمي في CatalogQueries (Infrastructure، ADR-0008).
public class GetProductsHandler : IRequestHandler<GetProductsQuery, PaginatedList<ProductDto>>
{
    private readonly ICatalogQueries _catalog;
    private readonly ITenantContext _tenant;

    public GetProductsHandler(ICatalogQueries catalog, ITenantContext tenant)
    {
        _catalog = catalog; _tenant = tenant;
    }

    public Task<PaginatedList<ProductDto>> Handle(GetProductsQuery q, CancellationToken ct) =>
        _catalog.SearchProductsAsync(
            new ProductSearch(q.Keyword, q.CategoryIds, q.MinPrice, q.MaxPrice, q.SortBy, q.OnSale),
            PageRequest.From(q), _tenant.RequireTenant().DefaultCulture, ct);
}
