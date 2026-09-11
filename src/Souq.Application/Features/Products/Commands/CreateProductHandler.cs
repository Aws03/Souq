using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Queries;
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
        var store = _tenant.RequireTenant();
        if (!CatalogTexts.HasCulture(cmd.Translations, store.DefaultCulture))
            return Result<int>.Failure(ProductRules.DefaultTranslationRequired(store.DefaultCulture));

        // الفئة من هذا المتجر فقط (المستودع مُرشَّح بالمتجر): معرّف فئة متجر آخر = غير موجودة.
        if (await _categories.GetByIdAsync(cmd.CategoryId, ct) is null)
            return Result<int>.Failure(Error.Validation("CategoryNotFound", "الفئة المحدّدة غير موجودة"));

        // الكيان يطبّق قواعده (النصوص، السعر، SKU، الحالة). السعر بعملة المتجر دائماً — لا عملة من العميل.
        var product = new Product(
            cmd.Slug is { Length: > 0 } slug ? slug : ProductSlugs.Suggest(cmd.Translations),
            cmd.CategoryId, CatalogTexts.ToDomain(cmd.Translations),
            new Money(cmd.Price, store.Currency), cmd.StockQuantity, cmd.Status, cmd.Sku,
            cmd.CompareAtPrice is decimal compareAt ? new Money(compareAt, store.Currency) : null,
            cmd.Brand, cmd.LowStockThreshold);
        product.SetVideoUrl(cmd.VideoUrl);

        // معرّف مقترَح يُفرَّد بلاحقة؛ معرّف أدخله المدير نفسه ⇒ تعارض صريح بدل تغييره بصمت.
        if (cmd.Slug is { Length: > 0 })
        {
            if (await _products.SlugExistsAsync(product.Slug, null, ct))
                return Result<int>.Failure(ProductRules.SlugTaken);
        }
        else
        {
            var baseSlug = product.Slug;
            for (var suffix = 2; await _products.SlugExistsAsync(product.Slug, null, ct); suffix++)
                product.SetSlug($"{baseSlug[..Math.Min(baseSlug.Length, Product.SlugMaxLength - 6)]}-{suffix}");
        }
        if (product.Sku is { } sku && await _products.SkuExistsAsync(sku, null, ct))
            return Result<int>.Failure(ProductRules.SkuTaken);

        await _products.AddAsync(product, ct);
        await _uow.SaveChangesAsync(ct);   // يولّد Id

        // المخزون الابتدائي حركة توريد — سجلّ حركة المنتج يبدأ من نقطة معلومة.
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

// أخطاء وقواعد مشتركة بين إنشاء المنتج وتعديله.
internal static class ProductRules
{
    public static Error SlugTaken => Error.Conflict("ProductSlugTaken", "معرّف الرابط مستخدم لمنتج آخر في هذا المتجر");
    public static Error SkuTaken => Error.Conflict("SkuTaken", "SKU مستخدم لمنتج آخر في هذا المتجر");

    public static Error DefaultTranslationRequired(string culture) =>
        Error.Validation("DefaultTranslationRequired", $"اسم المنتج بلغة المتجر الافتراضية ({culture}) مطلوب");
}

// معرّف رابط مقترَح من الاسم اللاتيني (الإنجليزي أولاً)، وإلا "p-" + 8 خانات عشوائية (أسماء عربية بلا نقحرة).
public static class ProductSlugs
{
    public static string Suggest(IReadOnlyDictionary<string, CatalogTextInput>? translations)
    {
        var names = (translations ?? new Dictionary<string, CatalogTextInput>())
            .OrderBy(t => t.Key.Equals("en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(t => t.Value?.Name ?? "");

        foreach (var name in names)
        {
            var slug = string.Join('-', new string(name.ToLowerInvariant()
                    .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (slug.Length >= 2) return slug[..Math.Min(slug.Length, 100)].Trim('-');
        }
        return $"p-{Guid.NewGuid():N}"[..10];
    }
}
