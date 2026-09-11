using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

// تغيير كلمة المرور لحساب مسجّل: يتطلّب الحالية، ويُبطل كل الجلسات الأخرى (ختم الأمان + رموز التجديد)،
// ويعيد جلسة جديدة لهذا الجهاز كي لا يُطرد صاحب التغيير نفسه.
public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result<AuthSession>>;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(PasswordRules.MaxLength);
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("كلمة المرور الجديدة يجب أن تختلف عن الحالية");
    }
}

public class ChangePasswordHandler : IRequestHandler<ChangePasswordCommand, Result<AuthSession>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly AuthSessionIssuer _sessions;
    private readonly ISessionValidator _sessionValidator;
    private readonly ICurrentUser _currentUser;

    public ChangePasswordHandler(
        IUserRepository users, IPasswordHasher hasher, AuthSessionIssuer sessions,
        ISessionValidator sessionValidator, ICurrentUser currentUser)
    {
        _users = users; _hasher = hasher; _sessions = sessions; _sessionValidator = sessionValidator; _currentUser = currentUser;
    }

    public async Task<Result<AuthSession>> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_currentUser.RequireUserId(), ct);
        if (user is null)
            return Result<AuthSession>.Failure(Error.Unauthorized("Unauthenticated", "سجّل الدخول للمتابعة."));

        // 400 لا 401: الواجهة تعامل 401 كانتهاء جلسة وتسجّل الخروج — هنا مجرّد إدخال خاطئ.
        if (!_hasher.Verify(cmd.CurrentPassword, user.PasswordHash))
            return Result<AuthSession>.Failure(Error.Validation("CurrentPasswordIncorrect", "كلمة المرور الحالية غير صحيحة"));

        user.ChangePassword(_hasher.Hash(cmd.NewPassword));
        await _sessions.RevokeAllAsync(user.Id, "PasswordChanged", ct);
        var session = await _sessions.IssueAsync(user, familyId: null, ct);   // حفظ واحد: التغيير والإبطال والجلسة
        _sessionValidator.Forget(user.Id);
        return Result<AuthSession>.Success(session);
    }
}
