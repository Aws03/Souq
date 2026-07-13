using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Queries;

public record GetRelatedProductsQuery(int ProductId, int Count = 6) : IRequest<Result<List<ProductDto>>>;

public class GetRelatedProductsHandler : IRequestHandler<GetRelatedProductsQuery, Result<List<ProductDto>>>
{
    private readonly IProductRepository _products;
    public GetRelatedProductsHandler(IProductRepository products) => _products = products;

    public async Task<Result<List<ProductDto>>> Handle(GetRelatedProductsQuery q, CancellationToken ct)
    {
        var product = await _products.GetActiveByIdAsync(q.ProductId, ct);
        if (product is null)
            return Result<List<ProductDto>>.Failure("المنتج غير موجود", "NotFound");

        var related = await _products.GetRelatedAsync(product.Id, product.CategoryId, q.Count, ct);

        var dtos = related.Select(p => new ProductDto(
            p.Id, p.NameAr, p.NameEn, p.Description, p.Price.Amount, p.Price.Currency,
            p.StockQuantity, p.ImageUrl, p.VideoUrl, p.CategoryId, p.Category?.Name)).ToList();

        return Result<List<ProductDto>>.Success(dtos);
    }
}
