using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Queries;

public record GetProductByIdQuery(int Id) : IRequest<Result<ProductDto>>;

// غير موجود أو معطّل ⇒ 404 نفسه: المنتج المعطّل لا يُعرض للعميل.
public class GetProductByIdHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly ICatalogQueries _catalog;
    public GetProductByIdHandler(ICatalogQueries catalog) => _catalog = catalog;

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery q, CancellationToken ct)
    {
        var product = await _catalog.FindActiveProductAsync(q.Id, ct);
        return product is null
            ? Result<ProductDto>.Failure(Error.NotFound("المنتج غير موجود"))
            : Result<ProductDto>.Success(product);
    }
}
