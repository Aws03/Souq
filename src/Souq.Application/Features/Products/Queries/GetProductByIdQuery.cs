using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Queries;

public record GetProductByIdQuery(int Id) : IRequest<Result<ProductDto>>;

public class GetProductByIdHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly IProductRepository _products;
    public GetProductByIdHandler(IProductRepository products) => _products = products;

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery q, CancellationToken ct)
    {
        var p = await _products.GetActiveByIdAsync(q.Id, ct);
        if (p is null)
            return Result<ProductDto>.Failure("المنتج غير موجود", "NotFound");

        return Result<ProductDto>.Success(new ProductDto(
            p.Id, p.NameAr, p.NameEn, p.Description, p.Price.Amount, p.Price.Currency,
            p.StockQuantity, p.ImageUrl, p.CategoryId, p.Category?.Name));
    }
}
