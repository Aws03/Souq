using Souq.Application.Common.Interfaces;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Application.Features.Auth.Contracts;

namespace Souq.Application.Features.Auth;

// ============================================================================
// AuthSessionIssuer — مكان واحد يصدر الجلسة (رمز تجديد جديد + توكن وصول) لكل مداخلها: الدخول،
// التسجيل، التجديد، وتغيير كلمة المرور. صنف ملموس بلا واجهة (لا بديل له — Architecture.md §11).
// يحفظ تغييرات وحدة العمل المعلّقة مع الرمز الجديد في حفظ واحد (مثلاً: RecordSuccessfulLogin).
// ============================================================================
public sealed class AuthSessionIssuer
{
    private readonly IRefreshTokenRepository _tokens;
    private readonly IAccountProfiles _profiles;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public AuthSessionIssuer(
        IRefreshTokenRepository tokens, IAccountProfiles profiles, IJwtTokenGenerator jwt,
        IUnitOfWork uow, TimeProvider clock)
    {
        _tokens = tokens; _profiles = profiles; _jwt = jwt; _uow = uow; _clock = clock;
    }

    // familyId: null ⇒ جلسة جديدة؛ قيمة ⇒ الحلقة التالية في عائلة قائمة (تدوير رمز التجديد).
    public async Task<AuthSession> IssueAsync(User user, Guid? familyId, CancellationToken ct)
    {
        // ملفات العملاء بيانات متجر: حساب المنصّة لا ملف له (والاستعلام في نطاق المنصّة يرمي أصلاً).
        var customerId = user.BelongsToPlatform ? null : await _profiles.FindIdForAccountAsync(user.Id, ct);

        var (refresh, rawRefresh) = RefreshToken.Issue(
            user, familyId ?? Guid.NewGuid(), _clock.GetUtcNow().UtcDateTime, _jwt.RefreshTokenLifetime);
        await _tokens.AddAsync(refresh, ct);
        await _uow.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = _jwt.Generate(user, customerId);
        return new AuthSession(
            new AuthResponse(accessToken, expiresAt, UserInfo.From(user, customerId)), rawRefresh, refresh.ExpiresAt);
    }

    // كل جلسات الحساب النشطة (تغيير أو إعادة تعيين كلمة المرور) — بلا حفظ؛ المستدعي يحفظ مع تغييره.
    public async Task RevokeAllAsync(int userId, string reason, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var token in await _tokens.ListActiveForUserAsync(userId, ct))
            token.Revoke(reason, now);
    }
}
