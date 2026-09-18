using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Auth.Commands;

// الرمز يصل من ملف تعريف ارتباط HttpOnly (الـ Controller يقرؤه) — لا من جسم الطلب ولا من JavaScript.
public record RefreshSessionCommand(string? RefreshToken) : IRequest<Result<AuthSession>>;

// ============================================================================
// RefreshSessionHandler — تدوير رمز التجديد مع كشف إعادة الاستخدام (ADR-0010):
//   فعّال        ⇒ يُستهلك (UsedAt) ويُصدَر رمز تالٍ في العائلة نفسها + توكن وصول جديد.
//   مستهلك للتو  ⇒ سباق تبويبات على الرمز نفسه (ضمن مهلة ثوانٍ) — رمز تالٍ آخر بلا إنذار.
//   مستهلك قديماً ⇒ نسخة مسروقة محتملة: تُبطَل العائلة كلها ويُدوَّر ختم الحساب فتسقط توكنات الوصول
//                   أيضاً، مع تحذير في السجل. المالك الحقيقي يسجّل الدخول من جديد.
//   غير ذلك      ⇒ 401 (منتهٍ، مُبطَل، مجهول، أو حساب معطّل).
// ============================================================================
public class RefreshSessionHandler : IRequestHandler<RefreshSessionCommand, Result<AuthSession>>
{
    private readonly IRefreshTokenRepository _tokens;
    private readonly IUserRepository _users;
    private readonly AuthSessionIssuer _sessions;
    private readonly ISessionValidator _sessionValidator;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly ILogger<RefreshSessionHandler> _logger;

    public RefreshSessionHandler(
        IRefreshTokenRepository tokens, IUserRepository users, AuthSessionIssuer sessions, ISessionValidator sessionValidator,
        IUnitOfWork uow, TimeProvider clock, ILogger<RefreshSessionHandler> logger)
    {
        _tokens = tokens; _users = users; _sessions = sessions; _sessionValidator = sessionValidator;
        _uow = uow; _clock = clock; _logger = logger;
    }

    public async Task<Result<AuthSession>> Handle(RefreshSessionCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.RefreshToken)) return Invalid();

        var token = await _tokens.GetByTokenAsync(cmd.RefreshToken, ct);
        if (token is null) return Invalid();

        var now = _clock.GetUtcNow().UtcDateTime;
        var user = await _users.GetByIdAsync(token.UserId, ct);
        var userActive = user is { Status: UserStatus.Active };

        if (userActive && token.IsActive(now))
        {
            token.MarkUsed(now);
            return Result<AuthSession>.Success(await _sessions.IssueAsync(user!, token.FamilyId, ct));
        }

        // ============================================================================
        // مهلة السباق لتبويبين يجدّدان معاً — لا لإحياء جلسة أُنهيت (M9).
        //
        // كانت هذه المهلة تُصدر جلسة جديدة لأيّ رمز استُهلك قبل عشر ثوان. و`RevokeAllAsync` (تغيير
        // كلمة المرور) يمرّ على الرموز **غير المستهلكة** وحدها، فالرمز الذي دوّره جهازٌ آخر قبل ثوانٍ
        // يبقى `RevokedAt == null` — ويُقبل هنا. النتيجة: من يحمل ملفّ تعريف ارتباط ذلك الجهاز يصنع
        // جلسة صالحة تماماً خلال عشر ثوان من تغيير كلمة المرور، بالختم الجديد. ومهاجم يعرف ذلك يدوّر
        // رمزه كل خمس ثوان فلا يُخرجه تغييرُ كلمة المرور أبداً.
        //
        // الفارق بين السباق البريء والإبطال الصريح موجود في البيانات بلا عمود جديد: **سباقٌ بريء يعني
        // أنّ التدوير نجح، فالعائلة ما زالت تحمل رمزاً فعّالاً.** أمّا تغيير كلمة المرور أو الخروج أو
        // كشف إعادة الاستخدام فيُبطل ذلك الرمز الفعّال، فتبقى العائلة بلا فعّال — وهذا هو الشرط.
        //
        // (تغيير كلمة المرور هو الحالة المكشوفة وحدها: المحو والتعطيل يضعان الحالة `Disabled`،
        // فـ `userActive` كاذبة ولا يُبلغ هذا الفرع أصلاً.)
        // ============================================================================
        if (userActive && token.IsWithinReuseGrace(now))
        {
            if (await _tokens.ListActiveInFamilyAsync(token.FamilyId, ct) is { Count: > 0 })
                return Result<AuthSession>.Success(await _sessions.IssueAsync(user!, token.FamilyId, ct));

            // العائلة أُبطلت بين التدوير والآن: الجلسة أُنهيت. ولا يُعَدّ هذا سرقةً — لأنّ فرع الكشف
            // يدوّر الختم، فيطرد صاحبَ تغيير كلمة المرور من جلسته الجديدة بسبب تبويبٍ على جهاز أخرجه هو.
            return Invalid();
        }

        if (token.UsedAt is not null && token.RevokedAt is null)
        {
            foreach (var sibling in await _tokens.ListActiveInFamilyAsync(token.FamilyId, ct))
                sibling.Revoke("ReuseDetected", now);
            token.Revoke("ReuseDetected", now);
            user?.RotateSecurityStamp();
            await _uow.SaveChangesAsync(ct);
            if (user is not null) _sessionValidator.Forget(user.Id);

            _logger.LogWarning("Refresh token reuse detected for user {UserId}; session family revoked", token.UserId);
            return Result<AuthSession>.Failure(Error.Unauthorized("RefreshTokenReused",
                "انتهت جلستك لأسباب أمنية. سجّل الدخول مجدداً."));
        }

        return Invalid();
    }

    private static Result<AuthSession> Invalid() => Result<AuthSession>.Failure(
        Error.Unauthorized("InvalidRefreshToken", "انتهت الجلسة. سجّل الدخول مجدداً."));
}
