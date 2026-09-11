using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;

namespace Souq.Application.Features.Products.Queries;

public record GetRelatedProductsQuery(int ProductId, int Count = 6) : IRequest<Result<IReadOnlyList<ProductDto>>>;

public class GetRelatedProductsHandler : IRequestHandler<GetRelatedProductsQuery, Result<IReadOnlyList<ProductDto>>>
{
    private readonly ICatalogQueries _catalog;
    private readonly ITenantContext _tenant;

    public GetRelatedProductsHandler(ICatalogQueries catalog, ITenantContext tenant)
    {
        _catalog = catalog; _tenant = tenant;
    }

    public async Task<Result<IReadOnlyList<ProductDto>>> Handle(GetRelatedProductsQuery q, CancellationToken ct)
    {
        var related = await _catalog.FindRelatedProductsAsync(q.ProductId, q.Count, _tenant.RequireTenant().DefaultCulture, ct);
        return related is null
            ? Result<IReadOnlyList<ProductDto>>.Failure(Error.NotFound("المنتج غير موجود"))
            : Result<IReadOnlyList<ProductDto>>.Success(related);
    }
}
