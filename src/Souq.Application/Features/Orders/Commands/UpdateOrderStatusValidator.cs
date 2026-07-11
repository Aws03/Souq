using FluentValidation;

namespace Souq.Application.Features.Orders.Commands;

public class UpdateOrderStatusValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Action).IsInEnum();   // يرفض قيمة إجراء خارج القائمة
    }
}
