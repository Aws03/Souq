using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// UpdateProductHandler — ينسّق تحديث منتج قائم. قواعد الصحّة في الكيان والمدقّق. الخطوات:
//   1) نجلب المنتج بأبنائه — غير موجود ⇒ 404.
//   2) نتحقّق من كل ما يحتاج القاعدة قبل الحفظ (لغة المتجر، الفئة، تفرّد المعرّف وSKU) — فلا يُحفَظ تعديل جزئي.
//   3) نطبّق عبر أبواب الكيان، ونحفظ ذرّياً — وrowversion يغلق نافذة السباق المتبقّية.
// المخزون ليس هنا (المرحلة 6): تصحيحاته في وحدة Inventory.
// ============================================================================
public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public UpdateProductHandler(
        IProductRepository products, ICategoryRepository categories, ITenantContext tenant, IUnitOfWork uow)
    {
        _products = products; _categories = categories; _tenant = tenant; _uow = uow;
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

        // السعر بعملة المنتج الحالية (عملة المتجر لحظة إنشائه) — لا عملة من العميل.
        var currency = product.Price.Currency;
        product.SetSlug(cmd.Slug);
        product.SetTexts(CatalogTexts.ToDomain(cmd.Translations));
        product.MoveToCategory(cmd.CategoryId);
        product.SetPricing(new Money(cmd.Price, currency),
            cmd.CompareAtPrice is decimal compareAt ? new Money(compareAt, currency) : null, cmd.Sku,
            cmd.Cost is decimal cost ? new Money(cost, currency) : null);
        product.SetBrand(cmd.Brand);
        product.SetVideoUrl(cmd.VideoUrl);

        if (await _products.SlugExistsAsync(product.Slug, product.Id, ct))
            return Result.Failure(ProductRules.SlugTaken);
        if (product.Sku is { } sku && await _products.SkuExistsAsync(sku, product.Id, ct))
            return Result.Failure(ProductRules.SkuTaken);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
