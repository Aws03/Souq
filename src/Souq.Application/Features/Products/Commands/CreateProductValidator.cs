using FluentValidation;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// التحقّق من المدخلات (Validation) — لماذا في ملف منفصل؟
// نفصل قواعد "صحّة المدخلات الشكلية" (اسم غير فارغ، سعر موجب) عن منطق العمل.
// FluentValidation يجمع كل القواعد في مكان واحد واضح، وسيُطبَّق تلقائياً قبل
// وصول الأمر للمعالج (عبر سلوك Pipeline سنضيفه). هذا يعني أن المعالج يثق دائماً
// بأن مدخلاته صحيحة شكلياً — فصل اهتمامات نظيف.
// ============================================================================
public class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).MaximumLength(200);
        RuleFor(x => x.VideoUrl).MaximumLength(500);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
    }
}
