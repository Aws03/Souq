using FluentValidation;
using Souq.Domain.Entities;

namespace Souq.Application.Features.Orders.Commands;

public class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        // عنوان من الدفتر أو عنوان نصّي — أحدهما مطلوب.
        RuleFor(x => x.ShippingAddress).NotEmpty().MaximumLength(Order.ShippingAddressMaxLength).When(x => x.ShippingAddressId is null);
        RuleFor(x => x.ShippingAddressId).GreaterThan(0).When(x => x.ShippingAddressId is not null);
        RuleFor(x => x.Items).NotEmpty().WithMessage("لا يمكن إنشاء طلب فارغ");
        RuleFor(x => x.CouponCode).MaximumLength(50).When(x => x.CouponCode is not null);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.ProductId).GreaterThan(0);
        });
    }
}
