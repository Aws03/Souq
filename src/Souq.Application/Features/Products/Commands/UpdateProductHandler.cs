using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// UpdateProductHandler — ينسّق تحديث منتج قائم. قواعد الصحّة في الكيان والمدقّق. الخطوات:
//   1) نجلب المنتج بأبنائه — غير موجود ⇒ 404.
//   2) نتحقّق من كل ما يحتاج القاعدة قبل أي تعديل (لغة المتجر، الفئة، تفرّد المعرّف وSKU، المخزون كما رآه المدير)
//      — فلا يُحفَظ تعديل جزئي مع رفض لاحق.
//   3) نطبّق عبر أبواب الكيان، ونحفظ ذرّياً — وrowversion يغلق نافذة السباق المتبقّية.
// ============================================================================
public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IStockMovementRepository _stockMovements;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public UpdateProductHandler(
        IProductRepository products, ICategoryRepository categories,
        IStockMovementRepository stockMovements, ITenantContext tenant, IUnitOfWork uow)
    {
        _products = products; _categories = categories;
        _stockMovements = stockMovements; _tenant = tenant; _uow = uow;
    }

    public async Task<Result> Handle(UpdateProductCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.Id, ct);
        if (product is null)
            return Result.Failure(Error.NotFound("المنتج غير موجود"));

        var store = _tenant.RequireTenant();
        if (!CatalogTexts.HasCulture(cmd.Translations, store.DefaultCulture))
            return Result.Failure(ProductRules.DefaultTranslationRequired(store.DefaultCulture));

        if (await _categories.GetByIdAsync(cmd.CategoryId, ct) is null)
            return Result.Failure(Error.Validation("CategoryNotFound", "الفئة المحدّدة غير موجودة"));

        // compare-and-set: تعيين المخزون فقط إن كان لا يزال كما رآه المدير.
        if (cmd.StockQuantity is not null && cmd.ExpectedStockQuantity != product.StockQuantity)
            return Result.Failure(Error.Conflict("StockChanged",
                $"تغيّر المخزون منذ فتح النموذج (الحالي الآن {product.StockQuantity}). أعد تحميل المنتج ثم عدّل."));

        // السعر بعملة المنتج الحالية (عملة المتجر لحظة إنشائه) — لا عملة من العميل.
        var currency = product.Price.Currency;
        product.SetSlug(cmd.Slug);
        product.SetTexts(CatalogTexts.ToDomain(cmd.Translations));
        product.MoveToCategory(cmd.CategoryId);
        product.SetPricing(new Money(cmd.Price, currency),
            cmd.CompareAtPrice is decimal compareAt ? new Money(compareAt, currency) : null, cmd.Sku);
        product.SetBrand(cmd.Brand);
        product.SetVideoUrl(cmd.VideoUrl);
        if (cmd.LowStockThreshold is int threshold)
            product.SetLowStockThreshold(threshold);

        if (await _products.SlugExistsAsync(product.Slug, product.Id, ct))
            return Result.Failure(ProductRules.SlugTaken);
        if (product.Sku is { } sku && await _products.SkuExistsAsync(sku, product.Id, ct))
            return Result.Failure(ProductRules.SkuTaken);

        // تعديل المخزون من الإدارة حركة تصحيح بإشارة الفارق — تُسجَّل فقط إن تغيّر فعلاً.
        if (cmd.StockQuantity is int newStock)
        {
            var stockDelta = newStock - product.StockQuantity;
            product.SetStock(newStock);
            if (stockDelta != 0)
                await _stockMovements.AddAsync(
                    StockMovement.For(product, StockMovementType.Adjustment, stockDelta, "تعديل يدوي من الإدارة"), ct);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
