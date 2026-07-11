using FluentValidation;

namespace Souq.Application.Features.Auth.Commands;

public class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        // حد أدنى معقول لكلمة المرور. قواعد أعقد (رموز/أرقام) تُضاف عند الحاجة.
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}
