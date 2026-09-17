using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// خيارات المنتج ومتغيّراته في الإدارة (P-08a، ADR-0040؛ catalog.manage، مدقَّقة). القواعد كلها في Product (الحدود، التفرّد،
// التركيبات، الافتراضي النشط)؛ هنا التنسيق وحده:
//   • المنتج من مستودع مُرشَّح بالمتجر، والمتغيّر والقيم تُحلّ داخل ذلك المنتج — معرّف منتج آخر أو متجر آخر ⇒ 404 أو رفض.
//   • أسماء الخيارات والقيم بلغة المتجر الافتراضية شرط: منها تُبنى لقطة وصف المتغيّر في سطر الطلب.
//   • كل تعديل يمرّ بحارس تزامن الجذر (GuardConcurrentEdit): مديران على المنتج نفسه ⇒ أحدهما 409 لا تركيبة ناقصة.
// ============================================================================

public sealed record ProductOptionInput(
    int? Id, IReadOnlyDictionary<string, string> Names, IReadOnlyList<ProductOptionValueInput> Values, int? ExistingVariantsValue = null);

public sealed record ProductOptionValueInput(int? Id, IReadOnlyDictionary<string, string> Names);

// ── تعريف الخيارات ───────────────────────────────────────────────────────────

// الخيارات كاملة بترتيبها: قائم بمعرّفه (تعديل الاسم والموضع والقيم)، جديد بلا معرّف، والغائب يُحذف إن سمحت القواعد.
public record SetProductOptionsCommand(int ProductId, IReadOnlyList<ProductOptionInput> Options) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.options-set", "Product", ProductId.ToString(),
        Metadata: new Dictionary<string, object?>
        {
            ["options"] = Options?.Count ?? 0,
            ["values"] = Options?.Sum(o => o?.Values?.Count ?? 0) ?? 0,
        });
}

public sealed class SetProductOptionsValidator : AbstractValidator<SetProductOptionsCommand>
{
    public SetProductOptionsValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Options).NotNull();
        RuleFor(x => x.Options.Count).LessThanOrEqualTo(Product.MaxOptions).When(x => x.Options is not null)
            .WithName("options").WithErrorCode("TooManyOptions");
        RuleForEach(x => x.Options).NotNull().ChildRules(option =>
        {
            option.RuleFor(o => o.Id).GreaterThan(0).When(o => o.Id.HasValue);
            option.RuleFor(o => o.Names).NotEmpty();
            option.RuleForEach(o => o.Names).ChildRules(ProductVariantInputRules.Name);
            option.RuleFor(o => o.Values).NotEmpty();
            option.RuleFor(o => o.Values.Count).LessThanOrEqualTo(Product.MaxValuesPerOption).When(o => o.Values is not null)
                .WithName("values").WithErrorCode("TooManyOptionValues");
            option.RuleForEach(o => o.Values).NotNull().ChildRules(value =>
            {
                value.RuleFor(v => v.Id).GreaterThan(0).When(v => v.Id.HasValue);
                value.RuleFor(v => v.Names).NotEmpty();
                value.RuleForEach(v => v.Names).ChildRules(ProductVariantInputRules.Name);
            });
            option.RuleFor(o => o.ExistingVariantsValue).GreaterThanOrEqualTo(0).When(o => o.ExistingVariantsValue.HasValue);
        });
    }
}

public class SetProductOptionsHandler : IRequestHandler<SetProductOptionsCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public SetProductOptionsHandler(IProductRepository products, ITenantContext tenant, IUnitOfWork uow)
    {
        _products = products; _tenant = tenant; _uow = uow;
    }

    public async Task<Result> Handle(SetProductOptionsCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is null) return Result.Failure(Error.NotFound("المنتج غير موجود"));

        var culture = _tenant.RequireTenant().DefaultCulture;
        if (cmd.Options.Any(o => !ProductVariantInputRules.HasCulture(o.Names, culture)
                                 || o.Values.Any(v => !ProductVariantInputRules.HasCulture(v.Names, culture))))
            return Result.Failure(Error.Validation("DefaultTranslationRequired",
                $"أسماء الخيارات وقيمها بلغة المتجر الافتراضية ({culture}) مطلوبة"));

        product.SetOptions(cmd.Options.Select(o => new ProductOptionDefinition(
            o.Id, o.Names, o.Values.Select(v => new ProductOptionValueDefinition(v.Id, v.Names)).ToList(),
            o.ExistingVariantsValue)).ToList());

        _products.GuardConcurrentEdit(product);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── إنشاء المتغيّرات ──────────────────────────────────────────────────────────

// متغيّر جديد: قيمة من كل خيار، تسعيره بعملة المنتج، ومخزونه الابتدائي — يُفتح في وحدة Inventory في المعاملة نفسها.
public sealed record NewProductVariantInput(
    IReadOnlyList<int> OptionValueIds, decimal Price, decimal? CompareAtPrice = null, string? Sku = null,
    int InitialStock = 0, int LowStockThreshold = InventoryItem.DefaultLowStockThreshold, bool IsActive = true);

// دفعة متغيّرات (منها "أنشئ التركيبات الناقصة") تُنشأ كلها أو لا شيء. يعيد معرّفاتها بترتيب المدخل.
public record CreateProductVariantsCommand(int ProductId, IReadOnlyList<NewProductVariantInput> Variants)
    : IRequest<Result<IReadOnlyList<int>>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.variants-created", "Product", ProductId.ToString(),
        Metadata: new Dictionary<string, object?>
        {
            ["count"] = Variants?.Count ?? 0,
            ["skus"] = Variants?.Select(v => v?.Sku).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
        });
}

public sealed class CreateProductVariantsValidator : AbstractValidator<CreateProductVariantsCommand>
{
    public CreateProductVariantsValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Variants).NotEmpty();
        RuleFor(x => x.Variants.Count).LessThanOrEqualTo(Product.MaxVariants).When(x => x.Variants is not null)
            .WithName("variants").WithErrorCode("TooManyVariants");
        RuleForEach(x => x.Variants).NotNull().ChildRules(variant =>
        {
            variant.RuleFor(v => v.OptionValueIds).NotEmpty();
            variant.RuleFor(v => v.OptionValueIds.Count).LessThanOrEqualTo(Product.MaxOptions).When(v => v.OptionValueIds is not null)
                .WithName("optionValueIds");
            variant.RuleForEach(v => v.OptionValueIds).GreaterThan(0);
            ProductVariantInputRules.Pricing(variant, v => v.Price, v => v.CompareAtPrice, v => v.Sku);
            variant.RuleFor(v => v.InitialStock).GreaterThanOrEqualTo(0);
            variant.RuleFor(v => v.LowStockThreshold).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreateProductVariantsHandler : IRequestHandler<CreateProductVariantsCommand, Result<IReadOnlyList<int>>>
{
    private readonly IProductRepository _products;
    private readonly IVariantStockInitializer _stock;
    private readonly IUnitOfWork _uow;

    public CreateProductVariantsHandler(IProductRepository products, IVariantStockInitializer stock, IUnitOfWork uow)
    {
        _products = products; _stock = stock; _uow = uow;
    }

    public async Task<Result<IReadOnlyList<int>>> Handle(CreateProductVariantsCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is null) return Result<IReadOnlyList<int>>.Failure(Error.NotFound("المنتج غير موجود"));

        // العملة من المنتج (عملة المتجر لحظة إنشائه) — لا عملة من العميل. قيم لا تخصّ هذا المنتج يرفضها Product.
        var currency = product.Price.Currency;
        var created = cmd.Variants.Select(input => (Input: input, Variant: product.AddVariant(
            input.OptionValueIds, new Money(input.Price, currency),
            input.CompareAtPrice is decimal compareAt ? new Money(compareAt, currency) : null,
            input.Sku, input.IsActive))).ToList();

        foreach (var sku in created.Select(c => c.Variant.Sku).OfType<string>())
            if (await _products.SkuExistsAsync(sku, product.Id, ct))
                return Result<IReadOnlyList<int>>.Failure(ProductRules.SkuTaken);

        // المتغيّرات ومخزونها معاً أو لا شيء: الحفظ يولّد المعرّفات، ثم تفتح Inventory مخزون كلٍّ منها (المنفذ نفسه للمنتج الجديد).
        _products.GuardConcurrentEdit(product);
        await _uow.InTransactionAsync(async () =>
        {
            await _uow.SaveChangesAsync(ct);
            foreach (var (input, variant) in created)
                await _stock.InitializeAsync(product.Id, variant.Id, input.InitialStock, input.LowStockThreshold, ct);
        }, ct);

        return Result<IReadOnlyList<int>>.Success(created.Select(c => c.Variant.Id).ToList());
    }
}

// ── تعديل متغيّر ──────────────────────────────────────────────────────────────

public record UpdateProductVariantCommand(int ProductId, int VariantId, decimal Price, decimal? CompareAtPrice = null, string? Sku = null)
    : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.variant-updated", "ProductVariant", VariantId.ToString(),
        Metadata: new Dictionary<string, object?> { ["productId"] = ProductId, ["price"] = Price, ["sku"] = Sku });
}

public sealed class UpdateProductVariantValidator : AbstractValidator<UpdateProductVariantCommand>
{
    public UpdateProductVariantValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.VariantId).GreaterThan(0);
        ProductVariantInputRules.Pricing(this, x => x.Price, x => x.CompareAtPrice, x => x.Sku);
    }
}

public class UpdateProductVariantHandler : IRequestHandler<UpdateProductVariantCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public UpdateProductVariantHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result> Handle(UpdateProductVariantCommand cmd, CancellationToken ct)
    {
        if (await ProductVariantTarget.LoadAsync(_products, cmd.ProductId, cmd.VariantId, ct) is not { } product)
            return Result.Failure(ProductVariantTarget.NotFound);

        var currency = product.Price.Currency;
        product.UpdateVariant(cmd.VariantId, new Money(cmd.Price, currency),
            cmd.CompareAtPrice is decimal compareAt ? new Money(compareAt, currency) : null, cmd.Sku);

        if (product.FindVariant(cmd.VariantId)!.Sku is { } sku && await _products.SkuExistsAsync(sku, product.Id, ct))
            return Result.Failure(ProductRules.SkuTaken);

        _products.GuardConcurrentEdit(product);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── تفعيل وتعطيل، والافتراضي ─────────────────────────────────────────────────

// لا حذف: التعطيل يُخرج المتغيّر من البيع ويُبقي مراجعه (الطلبات، المخزون، السلال). الافتراضي لا يُعطَّل (Product يرفض).
public record SetProductVariantStatusCommand(int ProductId, int VariantId, bool IsActive) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new(
        IsActive ? "catalog.product.variant-activated" : "catalog.product.variant-deactivated", "ProductVariant", VariantId.ToString(),
        Metadata: new Dictionary<string, object?> { ["productId"] = ProductId });
}

public sealed class SetProductVariantStatusValidator : AbstractValidator<SetProductVariantStatusCommand>
{
    public SetProductVariantStatusValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.VariantId).GreaterThan(0);
    }
}

public class SetProductVariantStatusHandler : IRequestHandler<SetProductVariantStatusCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public SetProductVariantStatusHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result> Handle(SetProductVariantStatusCommand cmd, CancellationToken ct)
    {
        if (await ProductVariantTarget.LoadAsync(_products, cmd.ProductId, cmd.VariantId, ct) is not { } product)
            return Result.Failure(ProductVariantTarget.NotFound);

        if (cmd.IsActive) product.ActivateVariant(cmd.VariantId);
        else product.DeactivateVariant(cmd.VariantId);

        _products.GuardConcurrentEdit(product);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// المتغيّر الذي يمثّل المنتج لكل عميل لا يعرف المتغيّرات (السعر في القوائم) — نشط شرطاً.
public record SetDefaultProductVariantCommand(int ProductId, int VariantId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.default-variant-set", "ProductVariant", VariantId.ToString(),
        Metadata: new Dictionary<string, object?> { ["productId"] = ProductId });
}

public sealed class SetDefaultProductVariantValidator : AbstractValidator<SetDefaultProductVariantCommand>
{
    public SetDefaultProductVariantValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.VariantId).GreaterThan(0);
    }
}

public class SetDefaultProductVariantHandler : IRequestHandler<SetDefaultProductVariantCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public SetDefaultProductVariantHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result> Handle(SetDefaultProductVariantCommand cmd, CancellationToken ct)
    {
        if (await ProductVariantTarget.LoadAsync(_products, cmd.ProductId, cmd.VariantId, ct) is not { } product)
            return Result.Failure(ProductVariantTarget.NotFound);

        product.SetDefaultVariant(cmd.VariantId);
        _products.GuardConcurrentEdit(product);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ── مشترك ───────────────────────────────────────────────────────────────────

// المنتج بشرط أن يكون المتغيّر منه: منتج متجر آخر، أو متغيّر منتج آخر (ولو في المتجر نفسه) ⇒ 404 واحدة لا تكشف أيّهما.
internal static class ProductVariantTarget
{
    public static Error NotFound => Error.NotFound("المتغيّر غير موجود");

    public static async Task<Product?> LoadAsync(IProductRepository products, int productId, int variantId, CancellationToken ct) =>
        await products.GetByIdAsync(productId, ct) is { } product && product.FindVariant(variantId) is not null ? product : null;
}

internal static class ProductVariantInputRules
{
    public static void Name(InlineValidator<KeyValuePair<string, string>> name) =>
        name.RuleFor(n => n.Value).NotEmpty().MaximumLength(OptionTranslation.NameMaxLength);

    public static bool HasCulture(IReadOnlyDictionary<string, string>? names, string culture) =>
        names is not null && names.Any(n => string.Equals(n.Key?.Trim(), culture, StringComparison.OrdinalIgnoreCase)
                                            && !string.IsNullOrWhiteSpace(n.Value));

    public static void Pricing<T>(
        AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, decimal>> price,
        System.Linq.Expressions.Expression<Func<T, decimal?>> compareAt, System.Linq.Expressions.Expression<Func<T, string?>> sku)
    {
        var readPrice = price.Compile();
        validator.RuleFor(price).GreaterThan(0);
        validator.RuleFor(compareAt).Must((item, value) => value is not decimal c || c > readPrice(item))
            .WithMessage("سعر المقارنة (قبل الخصم) يجب أن يكون أعلى من السعر");
        validator.RuleFor(sku).MaximumLength(ProductVariant.SkuMaxLength);
    }
}
