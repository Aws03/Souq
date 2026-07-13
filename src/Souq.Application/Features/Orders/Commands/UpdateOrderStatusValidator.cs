using FluentValidation;

namespace Souq.Application.Features.Orders.Commands;

public class UpdateOrderStatusValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Action).IsInEnum();   // يرفض قيمة إجراء خارج القائمة
        RuleFor(x => x.Note).MaximumLength(300);
        RuleFor(x => x.TrackingNumber).MaximumLength(100);
        RuleFor(x => x.ShippingCarrier).MaximumLength(100);
    }
}
