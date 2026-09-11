using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

public class CreateProductHandler : IRequestHandler<CreateProductCommand, Result<int>>
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IStockMovementRepository _stockMovements;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public CreateProductHandler(
        IProductRepository products, ICategoryRepository categories, IStockMovementRepository stockMovements,
        ITenantContext tenant, IUnitOfWork uow)
    {
        _products = products; _categories = categories; _stockMovements = stockMovements; _tenant = tenant; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        // الفئة من هذا المتجر فقط (المستودع مُرشَّح بالمتجر): معرّف فئة متجر آخر = غير موجودة. كان
        // الإنشاء يعتمد على المفتاح الأجنبي وحده فيقبل أي فئة موجودة على المنصّة.
        if (await _categories.GetByIdAsync(cmd.CategoryId, ct) is null)
            return Result<int>.Failure(Error.Validation("CategoryNotFound", "الفئة المحدّدة غير موجودة"));

        // ننشئ الكيان عبر مُنشئه — فيطبّق قواعده الداخلية تلقائياً. السعر بعملة المتجر دائماً:
        // لا عملة يرسلها العميل، ولا عملة افتراضية واحدة لكل المتاجر.
        var product = new Product(
            cmd.NameAr, cmd.Description,
            new Money(cmd.Price, _tenant.RequireTenant().Currency),
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
