using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Notifications;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Notifications;

// ============================================================================
// رسائل الحساب (المرحلة 14): الرمز يُولَّد هنا لحظة الإرسال لا وقت الطلب — لا رمز خام في صندوق الصادر ولا في أي جدول (التجزئة
// وحدها على الحساب). التجزئة تُحفظ قبل الاتصال بالمزوّد (لا معاملة مفتوحة أثناءه، ADR-0021)، وإعادة المحاولة تولّد رمزاً جديداً
// يُبطل ما قبله — آخر رسالة تصل هي الصالحة. حساب تغيّر منذ الطلب (عُطّل، أكّد بريده، قبل دعوته) ⇒ لا رسالة.
// ============================================================================
public sealed class PasswordResetEmailHandler : INotificationMessageHandler<PasswordResetRequested>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly NotificationEmails _emails;
    private readonly TimeProvider _clock;

    public PasswordResetEmailHandler(IUserRepository users, IUnitOfWork uow, NotificationEmails emails, TimeProvider clock)
    {
        _users = users; _uow = uow; _emails = emails; _clock = clock;
    }

    public async Task HandleAsync(PasswordResetRequested message, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(message.UserId, ct);
        if (user is not { Status: UserStatus.Active }) return;

        var token = user.GenerateResetToken(_clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        await _emails.SendAsync(user.Email, EmailTemplate.PasswordReset, message.Origin,
            StorefrontLinks.PasswordReset(message.Origin, token), EmptyValues.Instance, ct);
    }
}

public sealed class EmailVerificationEmailHandler : INotificationMessageHandler<EmailVerificationRequested>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly NotificationEmails _emails;
    private readonly TimeProvider _clock;

    public EmailVerificationEmailHandler(IUserRepository users, IUnitOfWork uow, NotificationEmails emails, TimeProvider clock)
    {
        _users = users; _uow = uow; _emails = emails; _clock = clock;
    }

    public async Task HandleAsync(EmailVerificationRequested message, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(message.UserId, ct);
        if (user is null || user.EmailConfirmedAt is not null || user.Status != UserStatus.Active) return;

        var token = user.GenerateEmailVerificationToken(_clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        await _emails.SendAsync(user.Email, EmailTemplate.EmailVerification, message.Origin,
            StorefrontLinks.EmailVerification(message.Origin, token), EmptyValues.Instance, ct);
    }
}

public sealed class InvitationEmailHandler : INotificationMessageHandler<AccountInvited>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly NotificationEmails _emails;
    private readonly TimeProvider _clock;

    public InvitationEmailHandler(IUserRepository users, IUnitOfWork uow, NotificationEmails emails, TimeProvider clock)
    {
        _users = users; _uow = uow; _emails = emails; _clock = clock;
    }

    public async Task HandleAsync(AccountInvited message, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(message.UserId, ct);
        if (user is not { IsInvitationPending: true, Status: UserStatus.Active }) return;

        var token = user.RenewInvitation(_clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        await _emails.SendAsync(user.Email, EmailTemplate.Invitation, message.Origin,
            StorefrontLinks.Invitation(message.Origin, token),
            new Dictionary<string, string> { ["inviter"] = message.InviterName }, ct);
    }
}

internal static class EmptyValues
{
    public static readonly IReadOnlyDictionary<string, string> Instance = new Dictionary<string, string>();
}
