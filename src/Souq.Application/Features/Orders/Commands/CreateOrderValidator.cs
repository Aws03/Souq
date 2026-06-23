using FluentValidation;
namespace Souq.Application.Features.Orders.Commands;

public class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ShippingAddress).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("لا يمكن إنشاء طلب فارغ");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.ProductId).GreaterThan(0);
        });
    }
}
