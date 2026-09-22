using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Security;
using Souq.Domain.Identity;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Security;

// ============================================================================
// StoreSessionRevoker — إبطال جلسات متجر كاملةً عند أرشفته (TD-66، منفذه `IStoreSessionRevoker`).
//
// **شرط المتجر مكتوب بيد في كل جملة، لأن المرشّح مُتجاوَز.** `User` و`RefreshToken` مُرشَّحان بنطاق
// الطلب، والمعالج يعمل في نطاق المنصّة — فلا بديل عن `IgnoreQueryFilters` و`TenantId == tenantId`.
// وهي القاعدة نفسها التي يحملها `PlatformQueries` في رأسه: كل استعلام يخصّ متجراً يحمل شرطه صريحاً.
//
// **ولماذا كتابة مجمّعة؟** لأن البديل تحميلُ كل حسابات المتجر في الذاكرة — وهي عملاؤه، لا موظّفوه
// وحدهم، فقد تكون آلافاً. جملتان ثابتتا التكلفة تفعلان ما تفعله حلقةٌ بلا حدّ. الطوابع المفوَّتة
// لا تضرّ: `SecurityStamp` علامةُ إصدار لا سجلّ، ورمز التجديد المُبطَل يحمل وقته في `RevokedAt`
// نفسه.
//
// **وختمٌ واحد لكل حسابات المتجر آمن، وهو الاختيار الذي يستحقّ تفسيراً.** الختم ليس سرّاً: هو
// مطالبةٌ داخل توكن **موقَّع**، وقيمته الوحيدة أن تختلف عمّا تحمله التوكنات القائمة. فمعرفة ختم
// حسابٍ آخر لا تصنع توكناً — التوقيع هو ما يمنع ذلك، لا سرّيةُ الختم. والبديل (ختمٌ عشوائي لكل
// صفّ) يلزمه تحميل الصفوف، وهو ما تتجنّبه هذه الجملة أصلاً.
//
// **حدٌّ مكتوب لا مُغطّى:** الأختام المخزَّنة تُسقَط على **هذه النسخة** فوراً (`ISessionValidator.ForgetAll`)،
// أمّا نسخةٌ أخرى من الخادم فتبقى تقبل توكناً قائماً حتى 30 ثانية — عمرُ ذاكرتها. القاعدة هي
// الحقيقة، والإبطال بين النسخ عملُ C4 لا هذا الملفّ.
// ============================================================================
internal sealed class StoreSessionRevoker : IStoreSessionRevoker
{
    private readonly AppDbContext _db;
    private readonly ISessionValidator _sessions;
    private readonly TimeProvider _clock;

    public StoreSessionRevoker(AppDbContext db, ISessionValidator sessions, TimeProvider clock)
    {
        _db = db; _sessions = sessions; _clock = clock;
    }

    public async Task<int> RevokeAllAsync(int tenantId, string reason, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var accounts = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, stamp), ct);

        await _db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), ct);

        // وإسقاط الأختام المخزَّنة — على هذه النسخة في الحال، وعلى غيرها خلال دورة استطلاع
        // (C4، ADR-0057). بلا هذا تبقى جلسةُ متجرٍ أُرشف مقبولةً حتى 30 ثانية، و«الأرشفة تُبطل
        // الجلسات» إمّا أن تكون صحيحة أو لا تُقال. الكلفة قراءةُ ختمٍ واحدة لكل حساب نشط بعدها،
        // والسبب نادر.
        await _sessions.ForgetAllAsync(ct);
        return accounts;
    }
}
