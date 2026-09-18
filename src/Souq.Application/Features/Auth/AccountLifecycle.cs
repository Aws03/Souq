using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Application.Features.Auth.Contracts;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth;

// ============================================================================
// تنفيذ `IAccountLifecycle` (M9): ما كانت وحدة Customers تفعله بالحساب مباشرةً، يفعله صاحبه.
//
// الفرق ليس ترتيباً: إبطال الجلسات كان يعيش في `CustomerErasure` — أي أنّ تغييراً في كيفية إبطال
// جلسةٍ كان يلزمه تعديل ملفٍّ في وحدة أخرى، ولا شيء يدلّ قارئ Identity على وجوده هناك.
// ============================================================================
public sealed class AccountLifecycle : IAccountLifecycle
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionValidator _sessions;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public AccountLifecycle(
        IUserRepository users, IRefreshTokenRepository tokens, IPasswordHasher hasher,
        ISessionValidator sessions, IUnitOfWork uow, TimeProvider clock)
    {
        _users = users; _tokens = tokens; _hasher = hasher; _sessions = sessions; _uow = uow; _clock = clock;
    }

    public async Task<bool> VerifyPasswordAsync(int userId, string password, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        // حسابٌ غير موجود يُعيد false لا استثناءً: المتصل يرفض بالرسالة نفسها، فلا يكشف الفرق.
        return user is not null && _hasher.Verify(password, user.PasswordHash);
    }

    public async Task RenameAsync(int userId, string fullName, CancellationToken ct) =>
        (await _users.GetByIdAsync(userId, ct))?.Rename(fullName);

    public async Task EraseAsync(int userId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is not null)
        {
            user.Erase();
            foreach (var token in await _tokens.ListActiveForUserAsync(userId, ct))
                token.Revoke("Erased", now);
        }

        // حفظٌ واحد يشمل ما جهّزه المستدعي (الملفّ والسلة والمفضّلة) — المحو ذرّة واحدة.
        await _uow.SaveChangesAsync(ct);
        // ثم النسيان: بعد الإيداع، فلا يعيد طلبٌ متزامن تخزين الختم القديم.
        if (user is not null) _sessions.Forget(userId);
    }
}
