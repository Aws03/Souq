using Souq.Domain.Common;

namespace Souq.Domain.Identity;

// ============================================================================
// RefreshToken — جلسة طويلة تجدّد توكن الوصول القصير (15 دقيقة) دون كلمة المرور (ADR-0010).
//   • القيمة الخام 256 بت عشوائية في ملف تعريف ارتباط HttpOnly؛ القاعدة تحمل تجزئتها فقط.
//   • التدوير: كل تجديد يستهلك الرمز (UsedAt) ويصدر رمزاً جديداً في "العائلة" نفسها.
//   • إعادة الاستخدام: رمز مستهلَك يُقدَّم مجدداً بعد مهلة السباق القصيرة = سرقة محتملة ⇒ تُبطَل
//     العائلة كلها ويُدوَّر ختم المستخدم (Application — RefreshSessionHandler).
// المتجر يُختم من النطاق كأي صف متجر؛ رموز حسابات المنصّة بلا متجر.
// ============================================================================
public class RefreshToken : Entity, ITenantOrPlatformOwned
{
    // نافذة سباق مقبولة: تبويبان يجدّدان بالرمز نفسه في اللحظة نفسها ليسا سرقة.
    public static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);

    public int? TenantId { get; private set; }
    public bool BelongsToPlatform { get; private set; }
    public int UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    private RefreshToken() { }

    // يعيد الكيان والقيمة الخام (النسخة الواضحة الوحيدة — تُكتب في ملف تعريف الارتباط ولا تُخزَّن).
    public static (RefreshToken Token, string RawValue) Issue(User user, Guid familyId, DateTime utcNow, TimeSpan lifetime)
    {
        var raw = User.NewToken();
        var token = new RefreshToken
        {
            UserId = user.Id,
            BelongsToPlatform = user.BelongsToPlatform,
            TokenHash = User.HashToken(raw),
            FamilyId = familyId,
            ExpiresAt = utcNow.Add(lifetime),
        };
        return (token, raw);
    }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && UsedAt is null && ExpiresAt > utcNow;

    // استُهلك للتو بالتدوير ولم يُبطَل — تقديمه مجدداً ضمن المهلة سباق تبويبات لا سرقة.
    public bool IsWithinReuseGrace(DateTime utcNow) =>
        RevokedAt is null && UsedAt is { } used && utcNow - used <= ReuseGracePeriod && ExpiresAt > utcNow;

    public void MarkUsed(DateTime utcNow) => UsedAt ??= utcNow;

    public void Revoke(string reason, DateTime utcNow)
    {
        if (RevokedAt is not null) return;
        RevokedAt = utcNow;
        RevokedReason = reason;
    }
}
