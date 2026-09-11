using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

public record ForgotPasswordCommand(string Email) : IRequest<Result>;

public class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

// ============================================================================
// ForgotPasswordHandler — ينجح دائماً بلا استثناء (حتى حين لا يوجد حساب بهذا البريد في هذا المتجر):
// رسالة مختلفة كانت ستسمح بـ "تعداد" الحسابات (User Enumeration). الرابط على مضيف الطلب نفسه
// (IStorefrontLinks) فيصل عميل كل متجر لمتجره، وحدّ المعدّل في الـ API يمنع إغراق البريد.
// ============================================================================
public class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly IStorefrontLinks _links;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ForgotPasswordHandler(
        IUserRepository users, IEmailService email, IStorefrontLinks links, IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _email = email; _links = links; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(cmd.Email, ct);
        if (user is { Status: UserStatus.Active })
        {
            var token = user.GenerateResetToken(_clock.GetUtcNow().UtcDateTime);
            await _uow.SaveChangesAsync(ct);

            await _email.SendPasswordResetEmailAsync(user.Email, _links.PasswordReset(token), ct);
        }

        return Result.Success();
    }
}
