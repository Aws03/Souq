using FluentValidation;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

// تحقّق شكلي من مدخلات التحديث — يُطبَّق تلقائياً قبل المعالج عبر ValidationBehavior.
public class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(Product.SlugMaxLength);
        RuleFor(x => x.Translations).NotEmpty();
        RuleForEach(x => x.Translations).ChildRules(text =>
        {
            text.RuleFor(t => t.Value).NotNull();
            text.RuleFor(t => t.Value.Name).NotEmpty().MaximumLength(CatalogText.NameMaxLength).When(t => t.Value is not null);
        });
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.CompareAtPrice).GreaterThan(x => x.Price).When(x => x.CompareAtPrice.HasValue)
            .WithMessage("سعر المقارنة (قبل الخصم) يجب أن يكون أعلى من السعر");
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0).When(x => x.StockQuantity.HasValue);
        // تعيين المخزون يتطلّب القيمة التي رآها المدير — أساس المقارنة (compare-and-set).
        RuleFor(x => x.ExpectedStockQuantity).NotNull().When(x => x.StockQuantity.HasValue)
            .WithMessage("expectedStockQuantity مطلوب عند تعديل المخزون");
        RuleFor(x => x.ExpectedStockQuantity).GreaterThanOrEqualTo(0).When(x => x.ExpectedStockQuantity.HasValue);
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0).When(x => x.LowStockThreshold.HasValue);
        RuleFor(x => x.Sku).MaximumLength(ProductVariant.SkuMaxLength);
        RuleFor(x => x.Brand).MaximumLength(Product.BrandMaxLength);
        RuleFor(x => x.VideoUrl).MaximumLength(Product.VideoUrlMaxLength);
    }
}
