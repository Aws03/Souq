using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Security;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Application.Common.Exceptions;

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
// رمز جديد (إعادة إرسال، ويُحدَّث الاسم والدور — ورابط الدعوة السابقة يسقط فوراً)؛ لحساب مفعّل ⇒ EmailTaken. الحساب ورسالة
// الدعوة في وحدة واحدة (المرحلة 14): الرسالة في صندوق الصادر ورمزها يُولَّد عند إرسالها — لا انتظار لمزوّد البريد، وفشله يُعاد
// تلقائياً.
// ============================================================================
public sealed class AccountInvitations
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly INotificationOutbox _outbox;
    private readonly IStorefrontLinks _links;
    private readonly TimeProvider _clock;

    public AccountInvitations(
        IUserRepository users, IUnitOfWork uow, INotificationOutbox outbox, IStorefrontLinks links, TimeProvider clock)
    {
        _users = users; _uow = uow; _outbox = outbox; _links = links; _clock = clock;
    }

    // inviterName: اسم المتجر (أو المنصّة) في نصّ الرسالة. linkHost: مضيف صفحة القبول؛ null ⇒ مضيف الطلب.
    public async Task<Result<InvitationResult>> InviteAsync(
        string fullName, string email, string role, string inviterName, string? linkHost, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var existing = await _users.GetByEmailAsync(email, ct);

        User user;
        if (existing is null)
        {
            (user, _) = User.Invite(fullName, email, role, now);
            await _users.AddAsync(user, ct);
        }
        else if (existing.IsInvitationPending)
        {
            existing.Rename(fullName);
            existing.ChangeRole(role);
            existing.RenewInvitation(now);   // رابط الرسالة السابقة يسقط الآن، لا حين تُرسل الجديدة
            user = existing;
        }
        else
        {
            return Result<InvitationResult>.Failure(
                Error.Conflict("EmailTaken", "البريد الإلكتروني مستخدم لحساب مفعّل هنا"));
        }

        var origin = _links.Origin(linkHost);
        await _uow.InTransactionAsync(async () =>
        {
            await _uow.SaveChangesAsync(ct);   // معرّف الحساب الجديد قبل رسالته
            _outbox.Enqueue(new AccountInvited(user.Id, inviterName, origin));
            await _uow.SaveChangesAsync(ct);
        }, ct);
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

        // الحساب الذي يحرسه شرط "آخر مدير": فعّال، بالدور الأعلى، ودعوته ليست معلّقة.
        var guarded = user.Role == lastStandingRole && user is { Status: UserStatus.Active, IsInvitationPending: false };

        // المسار السريع: الحالة الشائعة (آخر مدير فعلاً) تُرفض برسالة واضحة بلا معاملة.
        if (guarded && await _users.CountActiveByRoleAsync(lastStandingRole, ct) <= 1)
            return Result.Failure(Error.BusinessRule("LastAdministrator", "لا يمكن إيقاف آخر حساب فعّال بهذا الدور"));

        // ── F-7: الفحص أعلاه وحده لا يكفي ────────────────────────────────────────────
        // طلبان متزامنان يوقفان مديرَين مختلفين: كلاهما يعدّ 2 قبل أن يكتب أيٌّ منهما، فيمرّان،
        // فيبقى المتجر بلا مدير. وrowversion على User لا يُنقذ لأن كلاً منهما يكتب صفّاً *آخر*.
        //
        // الحلّ هنا: نوقف الحساب ثم نعيد العدّ داخل المعاملة نفسها. تحت READ COMMITTED القافل —
        // وهو افتراضي SQL Server، ولا يُفعّل هذا المستودع RCSI في أي مكان — يضطرّ العدّ الثاني
        // إلى قراءة صفّ الطلب الآخر، فيحجزه قفله الحصري حتى يلتزم، فيرى النتيجة النهائية لا
        // القديمة. من يخسر السباق يرى صفراً ويتراجع كلياً.
        //
        // لماذا لا SERIALIZABLE: العدّ يمسح مدى الدور، فطلبان متزامنان يأخذان أقفال مدى مشتركة
        // ثم يطلبان الحصري — جمود (deadlock) يُنهي أحدهما بخطأ خادم بدل رفض عمل واضح. ولماذا لا
        // sp_getapplock: قفل مسمّى خاص بـ SQL Server يضيف مفهوماً جديداً لحلّ ما تحلّه المعاملة.
        try
        {
            await _uow.InTransactionAsync(async () =>
            {
                user.Disable();
                var now = _clock.GetUtcNow().UtcDateTime;
                foreach (var token in await _tokens.ListActiveForUserAsync(user.Id, ct))
                    token.Revoke("Disabled", now);
                await _uow.SaveChangesAsync(ct);

                if (guarded && await _users.CountActiveByRoleAsync(lastStandingRole, ct) == 0)
                    throw new LastAdministratorRace();
            }, ct);
        }
        catch (LastAdministratorRace)
        {
            return Result.Failure(Error.BusinessRule("LastAdministrator", "لا يمكن إيقاف آخر حساب فعّال بهذا الدور"));
        }
        catch (ConcurrencyConflictException) when (guarded)
        {
            // خسرنا السباق بجمود: معاملتنا رُجِعت كاملةً وحسابنا ما زال فعّالاً، والطلب الآخر
            // التزم. لا نخمّن النتيجة — نعيد العدّ الآن وقد استقرّ كل شيء: إن بقي واحد فنحن
            // نحاول إيقاف الأخير فعلاً، وهذا رفض عمل واضح لا خطأ خادم.
            if (await _users.CountActiveByRoleAsync(lastStandingRole, ct) <= 1)
                return Result.Failure(Error.BusinessRule("LastAdministrator", "لا يمكن إيقاف آخر حساب فعّال بهذا الدور"));
            throw;   // جمود لسبب آخر: يبقى 409 ورسالته "أعد المحاولة"
        }

        _sessions.Forget(user.Id);
        return Result.Success();
    }

    // إشارة داخلية للتراجع عن المعاملة وحدها — لا تعبر حدود هذا الصنف.
    private sealed class LastAdministratorRace : Exception;
}
