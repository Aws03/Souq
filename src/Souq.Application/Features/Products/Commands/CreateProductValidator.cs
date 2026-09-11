using FluentValidation;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// التحقّق الشكلي (FluentValidation، قبل المعالج): الحقول موجودة وضمن حدودها. قواعد القيم (لغات مدعومة، سعر المقارنة
// أعلى من السعر، صيغة SKU والمعرّف) يحرسها الكيان ويعيدها 422 برسالتها.
// ============================================================================
public class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Translations).NotEmpty();
        RuleForEach(x => x.Translations).ChildRules(text =>
        {
            text.RuleFor(t => t.Value).NotNull();
            text.RuleFor(t => t.Value.Name).NotEmpty().MaximumLength(CatalogText.NameMaxLength).When(t => t.Value is not null);
        });
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.CompareAtPrice).GreaterThan(x => x.Price).When(x => x.CompareAtPrice.HasValue)
            .WithMessage("سعر المقارنة (قبل الخصم) يجب أن يكون أعلى من السعر");
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Status).IsInEnum().NotEqual(ProductStatus.Archived);
        RuleFor(x => x.Sku).MaximumLength(ProductVariant.SkuMaxLength);
        RuleFor(x => x.Slug).MaximumLength(Product.SlugMaxLength);
        RuleFor(x => x.Brand).MaximumLength(Product.BrandMaxLength);
        RuleFor(x => x.VideoUrl).MaximumLength(Product.VideoUrlMaxLength);
    }
}
