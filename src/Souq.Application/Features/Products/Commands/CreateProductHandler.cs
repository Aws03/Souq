using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

public class CreateProductHandler : IRequestHandler<CreateProductCommand, Result<int>>
{
    private readonly IProductRepository _products;
    private readonly IStockMovementRepository _stockMovements;
    private readonly IUnitOfWork _uow;

    public CreateProductHandler(
        IProductRepository products, IStockMovementRepository stockMovements, IUnitOfWork uow)
    {
        _products = products; _stockMovements = stockMovements; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        // ننشئ الكيان عبر مُنشئه — فيطبّق قواعده الداخلية تلقائياً.
        var product = new Product(
            cmd.NameAr, cmd.Description,
            new Money(cmd.Price),          // يتحقّق Money من أن السعر غير سالب
            cmd.StockQuantity, cmd.ImageUrl, cmd.CategoryId, cmd.NameEn, cmd.VideoUrl,
            cmd.LowStockThreshold);

        await _products.AddAsync(product, ct);
        await _uow.SaveChangesAsync(ct);   // الحفظ الفعلي يحدث هنا، مرة واحدة (يولّد Id)

        // المخزون الابتدائي حركة توريد (Purchase) — كي يبدأ سجلّ حركة المنتج من
        // نقطة معلومة بدل الظهور فجأة. نسجّلها بعد أن يحصل المنتج على معرّفه.
        if (product.StockQuantity > 0)
        {
            await _stockMovements.AddAsync(
                StockMovement.For(product, StockMovementType.Purchase, product.StockQuantity,
                    "المخزون الابتدائي عند إنشاء المنتج"), ct);
            await _uow.SaveChangesAsync(ct);
        }

        return Result<int>.Success(product.Id);
    }
}
