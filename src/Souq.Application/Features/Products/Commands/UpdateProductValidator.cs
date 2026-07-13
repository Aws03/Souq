using FluentValidation;

namespace Souq.Application.Features.Products.Commands;

// تحقّق شكلي من مدخلات التحديث — يُطبَّق تلقائياً قبل المعالج عبر ValidationBehavior.
// نفس قواعد الإنشاء مع إضافة التحقّق من المعرّف، حتى يثق المعالج بصحّة مدخلاته.
public class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        // null مقبول (نُبقي الحدّ الحالي)؛ إن أُرسلت قيمة فيجب ألّا تكون سالبة.
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0)
            .When(x => x.LowStockThreshold.HasValue);
        RuleFor(x => x.ImageUrl).MaximumLength(500);
        RuleFor(x => x.VideoUrl).MaximumLength(500);
        RuleFor(x => x.CategoryId).GreaterThan(0);
    }
}
