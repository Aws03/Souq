using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Notifications;
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
    private readonly AccountWriter _writer;
    private readonly ISessionValidator _sessionValidator;
    private readonly ICurrentUser _currentUser;
    private readonly INotificationOutbox _outbox;
    private readonly IStorefrontLinks _links;

    public ChangePasswordHandler(
        IUserRepository users, IPasswordHasher hasher, AuthSessionIssuer sessions, AccountWriter writer,
        ISessionValidator sessionValidator, ICurrentUser currentUser,
        INotificationOutbox outbox, IStorefrontLinks links)
    {
        _users = users; _hasher = hasher; _sessions = sessions; _writer = writer;
        _sessionValidator = sessionValidator; _currentUser = currentUser; _outbox = outbox; _links = links;
    }

    // تغيير كلمة المرور يكتب في صفّ `User` الذي يحمل rowversion، فطلبان متزامنان يتسابقان ويخسر
    // أحدهما بـ 409 (F-25؛ قِيس: ستة متزامنة ⇒ 200 مرّة و409 خمساً). تُعاد المحاولة على الحالة
    // الملتزمة — بقراءةٍ جديدة وتحقّقٍ جديد من كلمة المرور الحالية، فالطلب الذي يخسر السباق يرى
    // التجزئة الجديدة ويُرفض بصدق ("الحالية غير صحيحة") بدل أن يكتب فوق تغييرٍ التزم قبله.
    // وإشعار التغيير يُنسى مع المحاولة الملغاة (UserRepository.Reset)، فلا يصل بريدان عن تغيير واحد.
    public Task<Result<AuthSession>> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        // التجزئة مرّة لكل قيمة لا مرّة لكل محاولة. وهنا للحدّين معاً معنى: إن لم تتغيّر التجزئة
        // (زحامٌ على دفتر الدخول) فالتحقّق محفوظ ورخيص، وإن تغيّرت (طلبٌ متزامن غيّرها) أُعيد
        // حسابه عليها — وهو ما يجعل الخاسر يُرفض بصدق بدل أن يكتب فوق تغييرٍ التزم قبله.
        var current = new PasswordCheck(_hasher, cmd.CurrentPassword);
        return _writer.SaveAsync(() => AttemptAsync(cmd, current, ct), ct);
    }

    private async Task<Result<AuthSession>> AttemptAsync(
        ChangePasswordCommand cmd, PasswordCheck current, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_currentUser.RequireUserId(), ct);
        if (user is null)
            return Result<AuthSession>.Failure(Error.Unauthorized("Unauthenticated", "سجّل الدخول للمتابعة."));

        // 400 لا 401: الواجهة تعامل 401 كانتهاء جلسة وتسجّل الخروج — هنا مجرّد إدخال خاطئ.
        if (!current.Matches(user.PasswordHash))
            return Result<AuthSession>.Failure(Error.Validation("CurrentPasswordIncorrect", "كلمة المرور الحالية غير صحيحة"));

        user.ChangePassword(_hasher.Hash(cmd.NewPassword));
        // يُخطَر صاحب الحساب دائماً (M15، ASVS 2.2.3) — في الحفظ نفسه، فلا تغييرٌ بلا إشعاره ولا العكس.
        _outbox.Enqueue(new PasswordChanged(user.Id, _links.Origin()));
        await _sessions.RevokeAllAsync(user.Id, "PasswordChanged", ct);
        var session = await _sessions.IssueAsync(user, familyId: null, ct);   // حفظ واحد: التغيير والإبطال والجلسة
        _sessionValidator.Forget(user.Id);
        return Result<AuthSession>.Success(session);
    }
}
