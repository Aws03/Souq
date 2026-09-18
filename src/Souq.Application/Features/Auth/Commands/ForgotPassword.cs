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
// (IStorefrontLinks) فيصل عميل كل متجر لمتجره.
//
// وحدّ المعدّل في الـ API **لا يكفي لمنع إغراق صندوق ضحيّة** كما كان هذا التعليق يقول: ذلك الحدّ مقسوم
// على مضيف المهاجم وعنوانه، فيحرس الخادم لا صاحب البريد. المهلة التي تحرس الضحيّة في المجال نفسه
// (`User.CanIssueResetToken`) — انظر تعليقها هناك.
// ============================================================================
public class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly INotificationOutbox _outbox;
    private readonly IStorefrontLinks _links;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ForgotPasswordHandler(
        IUserRepository users, INotificationOutbox outbox, IStorefrontLinks links, IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _outbox = outbox; _links = links; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(cmd.Email, ct);
        // المهلة تُفحص **هنا وحدها** (M15): هذا هو الموضع الذي يُعرف فيه أنّ هذا طلبٌ جديد. وفحصُها
        // عند الإرسال أيضاً جُرِّب وكُسِر: هناك لا يُفرَّق بين طلبٍ جديد وإعادة محاولةٍ لرسالةٍ فشل
        // مزوّدها، فتنصرف المحاولة الثانية عن رمزٍ أصدرته هي — ولا تصل الرسالة أبداً.
        // والردّ يبقى نجاحاً سواء أُودعت أو لم تُودَع: التفريق بينهما يكشف وجود الحساب.
        if (user is { Status: UserStatus.Active } && user.CanIssueResetToken(_clock.GetUtcNow().UtcDateTime))
        {
            // المرحلة 14: الرسالة في صندوق الصادر والرمز يُولَّد عند إرسالها — الطلب لا ينتظر مزوّد البريد، ولا رمز خام يُخزَّن.
            _outbox.Enqueue(new PasswordResetRequested(user.Id, _links.Origin()));
            await _uow.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
