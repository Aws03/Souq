using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

public record LoginCommand(string Email, string Password) : IRequest<Result<AuthSession>>;

public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

// ============================================================================
// LoginHandler — الدخول داخل نطاق المضيف: حسابات هذا المتجر على مضيفه، وحسابات المنصّة على مضيف
// المنصّة (المستودع مُرشَّح بالنطاق). رسالة واحدة وزمن واحد لبريد غير مسجّل ولكلمة مرور خاطئة (تحقّق
// صوري) — لا تعداد حسابات. القفل بعد خمس محاولات فاشلة متتالية، وحدّ المعدّل لكل مضيف وعنوان (API).
// الحساب المعطّل يُكشف فقط بعد كلمة مرور صحيحة.
// ============================================================================
public class LoginHandler : IRequestHandler<LoginCommand, Result<AuthSession>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly AuthSessionIssuer _sessions;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public LoginHandler(
        IUserRepository users, IPasswordHasher hasher, AuthSessionIssuer sessions, IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _hasher = hasher; _sessions = sessions; _uow = uow; _clock = clock;
    }

    public async Task<Result<AuthSession>> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(cmd.Email, ct);
        if (user is null)
        {
            _hasher.Verify(cmd.Password, "");   // الزمن نفسه لبريد غير مسجّل (IPasswordHasher)
            return InvalidCredentials();
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        if (user.IsLockedOut(now))
            return Result<AuthSession>.Failure(Error.Unauthorized("AccountLocked",
                "تجاوزت عدد المحاولات المسموح. حاول بعد قليل أو أعد تعيين كلمة المرور."));

        if (!_hasher.Verify(cmd.Password, user.PasswordHash))
        {
            user.RecordFailedLogin(now);
            await _uow.SaveChangesAsync(ct);
            return InvalidCredentials();
        }

        if (user.Status == UserStatus.Disabled)
            return Result<AuthSession>.Failure(Error.Unauthorized("AccountDisabled", "هذا الحساب معطّل."));

        user.RecordSuccessfulLogin(now);
        return Result<AuthSession>.Success(await _sessions.IssueAsync(user, familyId: null, ct));
    }

    private static Result<AuthSession> InvalidCredentials() => Result<AuthSession>.Failure(
        Error.Unauthorized("InvalidCredentials", "البريد الإلكتروني أو كلمة المرور غير صحيحة"));
}
