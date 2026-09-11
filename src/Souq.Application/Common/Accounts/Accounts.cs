using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Common.Accounts;

// ============================================================================
// حسابات الإدارة (مدير/موظّف متجر، حساب منصّة) — لبنات مشتركة بين منطقة المنصّة (تدعو مدير متجر، تدير
// حسابات المنصّة) وإدارة موظّفي المتجر. كلها تعمل في النطاق الحالي: مرشّح الحسابات يحصرها في متجر السياق
// أو في حسابات المنصّة — والمنصّة تكتب داخل متجر عبر ITenantScopeRunner لا بتجاوز المرشّح.
// ============================================================================

public sealed record AccountSummaryDto(
    int Id, string FullName, string Email, string Role, string Status,
    bool InvitationPending, bool EmailConfirmed, DateTime? LastLoginAt, DateTime CreatedAt);

// منفذ القراءة: حسابات النطاق الحالي بأدوار محدّدة، الأحدث أولاً.
public interface IAccountQueries
{
    Task<PaginatedList<AccountSummaryDto>> ListAsync(IReadOnlyCollection<string> roles, PageRequest page, CancellationToken ct);
}

public sealed record InvitationResult(int UserId, bool Renewed);

// ============================================================================
// دعوة حساب إدارة: حساب بلا كلمة مرور + رابط لاختيارها بالبريد. البريد نفسه لحساب ما زال بانتظار القبول ⇒
// رمز جديد (إعادة إرسال، ويُحدَّث الاسم والدور)؛ لحساب مفعّل ⇒ EmailTaken. الحفظ أولاً ثم البريد — لا
// معاملة مفتوحة أثناء اتصال بالمزوّد (ADR-0021)، وفشل المزوّد يُسجَّل ولا يُسقط الدعوة (تُعاد بإرسالها ثانيةً).
// ============================================================================
public sealed class AccountInvitations
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _email;
    private readonly IStorefrontLinks _links;
    private readonly TimeProvider _clock;

    public AccountInvitations(IUserRepository users, IUnitOfWork uow, IEmailService email, IStorefrontLinks links, TimeProvider clock)
    {
        _users = users; _uow = uow; _email = email; _links = links; _clock = clock;
    }

    // inviterName: اسم المتجر (أو المنصّة) في نصّ الرسالة. linkHost: مضيف صفحة القبول؛ null ⇒ مضيف الطلب.
    public async Task<Result<InvitationResult>> InviteAsync(
        string fullName, string email, string role, string inviterName, string? linkHost, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var existing = await _users.GetByEmailAsync(email, ct);

        User user;
        string token;
        if (existing is null)
        {
            (user, token) = User.Invite(fullName, email, role, now);
            await _users.AddAsync(user, ct);
        }
        else if (existing.IsInvitationPending)
        {
            existing.Rename(fullName);
            existing.ChangeRole(role);
            token = existing.RenewInvitation(now);
            user = existing;
        }
        else
        {
            return Result<InvitationResult>.Failure(
                Error.Conflict("EmailTaken", "البريد الإلكتروني مستخدم لحساب مفعّل هنا"));
        }

        await _uow.SaveChangesAsync(ct);
        await _email.SendInvitationAsync(user.Email, inviterName, _links.Invitation(token, linkHost), ct);
        return Result<InvitationResult>.Success(new InvitationResult(user.Id, Renewed: existing is not null));
    }
}

// ============================================================================
// تفعيل/إيقاف حساب إدارة بقواعد مشتركة: لا يوقف أحد نفسه، ولا يُوقف آخر حامل فعّال لدور الإدارة العليا في
// نطاقه (آخر مدير متجر، آخر مالك منصّة) — وإلا بقي النطاق بلا من يديره. الإيقاف يدوّر ختم الأمان (تسقط توكنات
// الوصول) ويُبطل رموز التجديد كلها في الحفظ نفسه. حساب خارج الأدوار المُدارة هنا (عميل) ⇒ غير موجود.
// ============================================================================
public sealed class AccountStatusChanger
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _tokens;
    private readonly IUnitOfWork _uow;
    private readonly ISessionValidator _sessions;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AccountStatusChanger(
        IUserRepository users, IRefreshTokenRepository tokens, IUnitOfWork uow, ISessionValidator sessions,
        ICurrentUser currentUser, TimeProvider clock)
    {
        _users = users; _tokens = tokens; _uow = uow; _sessions = sessions; _currentUser = currentUser; _clock = clock;
    }

    public async Task<Result> SetActiveAsync(
        int userId, bool active, Func<string, bool> managedRole, string lastStandingRole, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !managedRole(user.Role))
            return Result.Failure(Error.NotFound("الحساب غير موجود"));

        if (active)
        {
            user.Enable();
            await _uow.SaveChangesAsync(ct);
            return Result.Success();
        }

        if (user.Id == _currentUser.UserId)
            return Result.Failure(Error.BusinessRule("CannotDisableSelf", "لا يمكنك إيقاف حسابك بنفسك"));
        if (user.Role == lastStandingRole && user is { Status: UserStatus.Active, IsInvitationPending: false }
            && await _users.CountActiveByRoleAsync(lastStandingRole, ct) <= 1)
            return Result.Failure(Error.BusinessRule("LastAdministrator", "لا يمكن إيقاف آخر حساب فعّال بهذا الدور"));

        user.Disable();
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var token in await _tokens.ListActiveForUserAsync(user.Id, ct))
            token.Revoke("Disabled", now);
        await _uow.SaveChangesAsync(ct);
        _sessions.Forget(user.Id);
        return Result.Success();
    }
}
