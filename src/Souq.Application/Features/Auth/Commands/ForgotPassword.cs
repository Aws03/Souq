using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Notifications;
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
    private readonly INotificationOutbox _outbox;
    private readonly IStorefrontLinks _links;
    private readonly IUnitOfWork _uow;

    public ForgotPasswordHandler(IUserRepository users, INotificationOutbox outbox, IStorefrontLinks links, IUnitOfWork uow)
    {
        _users = users; _outbox = outbox; _links = links; _uow = uow;
    }

    public async Task<Result> Handle(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(cmd.Email, ct);
        if (user is { Status: UserStatus.Active })
        {
            // المرحلة 14: الرسالة في صندوق الصادر والرمز يُولَّد عند إرسالها — الطلب لا ينتظر مزوّد البريد، ولا رمز خام يُخزَّن.
            _outbox.Enqueue(new PasswordResetRequested(user.Id, _links.Origin()));
            await _uow.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
