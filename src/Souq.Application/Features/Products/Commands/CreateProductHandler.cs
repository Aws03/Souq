using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

public class CreateProductHandler : IRequestHandler<CreateProductCommand, Result<int>>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public CreateProductHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        // ننشئ الكيان عبر مُنشئه — فيطبّق قواعده الداخلية تلقائياً.
        var product = new Product(
            cmd.Name, cmd.Description,
            new Money(cmd.Price),          // يتحقّق Money من أن السعر غير سالب
            cmd.StockQuantity, cmd.ImageUrl, cmd.CategoryId);

        await _products.AddAsync(product, ct);
        await _uow.SaveChangesAsync(ct);   // الحفظ الفعلي يحدث هنا، مرة واحدة
        return Result<int>.Success(product.Id);
    }
}
