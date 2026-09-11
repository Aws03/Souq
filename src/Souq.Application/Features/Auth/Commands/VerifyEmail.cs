using FluentValidation;
using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Notifications;
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

// إعادة إرسال رابط التأكيد للمستخدم الحالي (حدّ المعدّل في الـ API). مؤكَّد مسبقاً ⇒ نجاح بلا بريد. الرسالة تُوضع في صندوق
// الصادر (المرحلة 14) والرمز يُولَّد عند إرسالها — الطلب لا ينتظر مزوّد البريد.
public record ResendVerificationCommand : IRequest<Result>;

public class ResendVerificationHandler : IRequestHandler<ResendVerificationCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;
    private readonly INotificationOutbox _outbox;
    private readonly IStorefrontLinks _links;
    private readonly IUnitOfWork _uow;

    public ResendVerificationHandler(
        IUserRepository users, ICurrentUser currentUser, INotificationOutbox outbox, IStorefrontLinks links, IUnitOfWork uow)
    {
        _users = users; _currentUser = currentUser; _outbox = outbox; _links = links; _uow = uow;
    }

    public async Task<Result> Handle(ResendVerificationCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_currentUser.RequireUserId(), ct);
        if (user is null)
            return Result.Failure(Error.Unauthorized("Unauthenticated", "سجّل الدخول للمتابعة."));
        if (user.EmailConfirmedAt is not null)
            return Result.Success();

        _outbox.Enqueue(new EmailVerificationRequested(user.Id, _links.Origin()));
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
