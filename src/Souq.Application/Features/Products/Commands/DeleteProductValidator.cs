using FluentValidation;

namespace Souq.Application.Features.Products.Commands;

// تحقّق شكلي من أمر الحذف. وإن كان قيد المسار {id:int} يحمي جزئياً، نُبقي القاعدة
// في طبقة Application كي تنطبق على أي مستدعٍ للأمر (لا الـ HTTP فقط) — اتساق كامل.
public class DeleteProductValidator : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}
