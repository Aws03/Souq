using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

// تأكيد البريد برمز الرسالة (استخدام واحد، 48 ساعة). غير مفروض للدخول أو الشراء اليوم — قرار تجاري
// موثّق (AuthenticationAndAuthorization.md)؛ الحالة ظاهرة في /api/auth/me لمن يحتاجها.
public record VerifyEmailCommand(string Token) : IRequest<Result>;

public class VerifyEmailValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailValidator() => RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
}

public class VerifyEmailHandler : IRequestHandler<VerifyEmailCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public VerifyEmailHandler(IUserRepository users, IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(VerifyEmailCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByVerificationTokenAsync(cmd.Token, ct);
        if (user is null)
            return Result.Failure(Error.BusinessRule("InvalidVerificationToken", "رابط التأكيد غير صالح أو استُخدم مسبقاً"));

        user.ConfirmEmail(_clock.GetUtcNow().UtcDateTime);   // منتهٍ ⇒ 422 VerificationTokenExpired
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// إعادة إرسال رابط التأكيد للمستخدم الحالي (حدّ المعدّل في الـ API). مؤكَّد مسبقاً ⇒ نجاح بلا بريد.
public record ResendVerificationCommand : IRequest<Result>;

public class ResendVerificationHandler : IRequestHandler<ResendVerificationCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailService _email;
    private readonly IStorefrontLinks _links;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ResendVerificationHandler(
        IUserRepository users, ICurrentUser currentUser, IEmailService email, IStorefrontLinks links,
        IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _currentUser = currentUser; _email = email; _links = links; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(ResendVerificationCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_currentUser.RequireUserId(), ct);
        if (user is null)
            return Result.Failure(Error.Unauthorized("Unauthenticated", "سجّل الدخول للمتابعة."));
        if (user.EmailConfirmedAt is not null)
            return Result.Success();

        var token = user.GenerateEmailVerificationToken(_clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        await _email.SendEmailVerificationAsync(user.Email, _links.EmailVerification(token), ct);
        return Result.Success();
    }
}
