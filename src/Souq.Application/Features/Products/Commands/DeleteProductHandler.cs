using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// DeleteProductHandler — أرشفة لا حذف (المرحلة 5، C7). المنتج مرتبط بسطور طلبات تاريخية وتقييمات؛ حذفه يكسر
// سلامة السجلّات. المؤرشف يختفي من المتجر (CatalogQueries: النشط فقط) ويبقى في قائمة الإدارة ويُستعاد منها.
// ============================================================================
public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public DeleteProductHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result> Handle(DeleteProductCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.Id, ct);
        if (product is null)
            return Result.Failure(Error.NotFound("المنتج غير موجود"));

        product.Archive();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
