using FluentValidation;
using Souq.Domain.Enums;

namespace Souq.Application.Features.Coupons.Commands;

public class CreateCouponValidator : AbstractValidator<CreateCouponCommand>
{
    public CreateCouponValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Value).GreaterThan(0);
        RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.Type == DiscountType.Percentage)
            .WithMessage("نسبة الخصم لا يجب أن تتجاوز 100");
        RuleFor(x => x.MaxUses).GreaterThan(0).When(x => x.MaxUses.HasValue);
        RuleFor(x => x.MinOrderAmount).GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue);
    }
}

public class UpdateCouponValidator : AbstractValidator<UpdateCouponCommand>
{
    public UpdateCouponValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Value).GreaterThan(0);
        RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.Type == DiscountType.Percentage)
            .WithMessage("نسبة الخصم لا يجب أن تتجاوز 100");
        RuleFor(x => x.MaxUses).GreaterThan(0).When(x => x.MaxUses.HasValue);
        RuleFor(x => x.MinOrderAmount).GreaterThanOrEqualTo(0).When(x => x.MinOrderAmount.HasValue);
    }
}

public class DeleteCouponValidator : AbstractValidator<DeleteCouponCommand>
{
    public DeleteCouponValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
