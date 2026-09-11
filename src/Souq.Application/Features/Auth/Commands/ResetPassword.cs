using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

public record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Result>;

public class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

// إعادة التعيين برمز البريد (استخدام واحد، صلاحية ساعتين): تفكّ القفل، وتُبطل كل الجلسات القائمة —
// من سرق جلسة يفقدها حين يستعيد صاحب البريد حسابه.
public class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly AuthSessionIssuer _sessions;
    private readonly ISessionValidator _sessionValidator;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ResetPasswordHandler(
        IUserRepository users, IPasswordHasher hasher, AuthSessionIssuer sessions, ISessionValidator sessionValidator,
        IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _hasher = hasher; _sessions = sessions; _sessionValidator = sessionValidator; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(ResetPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByResetTokenAsync(cmd.Token, ct);
        if (user is null)
            return Result.Failure(Error.BusinessRule("InvalidResetToken", "رابط إعادة التعيين غير صالح"));

        // رمز منتهٍ ⇒ الكيان يرمي InvalidPasswordResetException (422 مركزياً) قبل أي حفظ.
        user.ResetPassword(_hasher.Hash(cmd.NewPassword), _clock.GetUtcNow().UtcDateTime);
        await _sessions.RevokeAllAsync(user.Id, "PasswordReset", ct);
        await _uow.SaveChangesAsync(ct);
        _sessionValidator.Forget(user.Id);

        return Result.Success();
    }
}
