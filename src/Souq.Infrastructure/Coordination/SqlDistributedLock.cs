using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Coordination;

// ============================================================================
// قفلٌ يعبر النسخ، بجدولٍ في القاعدة (C4، [ADR-0057](0057)).
//
// **الآليةُ هي آليةُ صندوق الصادر بعينها**: تحديثٌ **مشروط** بجملةٍ واحدة يحجز مدّةً —
//
//     UPDATE Leases SET Owner=@me, ExpiresAt=@until
//     WHERE Name=@work AND (ExpiresAt IS NULL OR ExpiresAt <= @now OR Owner=@me)
//
// وصفٌّ واحد مُحدَّث = العقدُ لنا. والشرطُ والكتابةُ في **جملةٍ ذرّية واحدة**، فلا نافذةَ بين
// «رأيتُه حرّاً» و«أخذتُه» — وتفكيكُها إلى قراءةٍ فقرارٍ فكتابة يُعيد بالضبط السباقَ الذي وُجد
// القفلُ لمنعه، وهو الخطأ نفسه الذي تحذّر منه ADR-0049 في عدّادات الحصص.
//
// **و`Owner=@me` في الشرط هو ما يجعل التجديد مجّانياً**: حاملُ العقد يُمدّده بالجملة نفسها بلا
// مسارٍ ثانٍ، ومَن ليس حاملَه لا يستطيع تمديدَ ما لا يملك.
//
// **ولا `sp_getapplock`.** أقفالُ المحرّك كانت ستؤدّي الغرض، لكنّها SQL خام يمنعه
// `لا_SQL_خام_في_Infrastructure_خارج_الهجرات` بحقّ (ADR-0022): جملةٌ نصّية لا يراها مرشّحُ
// المستأجر ولا يقرؤها أحد. وهذا الجدولُ عالميٌّ بلا متجر أصلاً، فلا مرشّح عليه ولا يحتاجه.
// ============================================================================
internal sealed class SqlDistributedLock : IDistributedLock
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly InstanceIdentity _instance;
    private readonly ILogger<SqlDistributedLock> _logger;

    public SqlDistributedLock(
        AppDbContext db, TimeProvider clock, InstanceIdentity instance, ILogger<SqlDistributedLock> logger)
    {
        _db = db; _clock = clock; _instance = instance; _logger = logger;
    }

    public async Task<ILeaseHandle?> TryAcquireAsync(string work, TimeSpan duration, CancellationToken ct = default)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "مدّةُ العقد أكبر من صفر");

        var name = DistributedLease.ForWork(work).Name;

        // أوّلُ محاولةٍ على صفٍّ قائم. صفرُ صفوفٍ مُحدَّثة يعني أحدَ أمرين: لا صفَّ بعد، أو نسخةٌ
        // أخرى تحمله — ويُفرَّق بينهما بمحاولة الإنشاء.
        if (await TryTakeAsync(name, duration, ct)) return Handle(name, duration);

        if (await _db.DistributedLeases.AsNoTracking().AnyAsync(l => l.Name == name, ct)) return null;

        var created = DistributedLease.ForWork(name);
        _db.DistributedLeases.Add(created);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // نسخةٌ أخرى أنشأت الصفَّ في هذه اللحظة. النوعُ مقصود ولا يجوز ردُّه إلى
            // `DbUpdateException`: `AppDbContext` يترجم 2601/2627 إلى نوعٍ يرث `Exception`
            // مباشرةً — وهو المزلق نفسه الذي وقع في `OrderNumbers`.
            _db.Entry(created).State = EntityState.Detached;
        }

        // ومحاولةٌ ثانية على الصفّ أياً كان مَن أنشأه: صفٌّ جديد حرٌّ بالتعريف، وصفٌّ أنشأه غيرُنا
        // قد يكون أخذه — والشرطُ نفسه يحسم الحالتين.
        return await TryTakeAsync(name, duration, ct) ? Handle(name, duration) : null;
    }

    private async Task<bool> TryTakeAsync(string name, TimeSpan duration, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var until = now + duration;
        var owner = _instance.Id;

        return await _db.DistributedLeases
            .Where(l => l.Name == name
                        && (l.ExpiresAtUtc == null || l.ExpiresAtUtc <= now || l.Owner == owner))
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Owner, owner)
                .SetProperty(l => l.AcquiredAtUtc, now)
                .SetProperty(l => l.ExpiresAtUtc, until), ct) == 1;
    }

    private async Task<bool> ReleaseAsync(string name, CancellationToken ct)
    {
        var owner = _instance.Id;

        // **بشرط الملكية**: عقدٌ انتهى وأخذته نسخةٌ أخرى لا نُفرج عنه نحن — وإلّا أفرجنا عن قفلِ
        // غيرنا وهو يعمل تحته.
        return await _db.DistributedLeases
            .Where(l => l.Name == name && l.Owner == owner)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Owner, (string?)null)
                .SetProperty(l => l.ExpiresAtUtc, (DateTime?)null), ct) == 1;
    }

    private ILeaseHandle Handle(string name, TimeSpan duration) =>
        new Lease(this, name, duration, _logger);

    private sealed class Lease : ILeaseHandle
    {
        private readonly SqlDistributedLock _owner;
        private readonly ILogger _logger;
        private bool _released;

        public Lease(SqlDistributedLock owner, string work, TimeSpan duration, ILogger logger)
        {
            _owner = owner; Work = work; Duration = duration; _logger = logger;
        }

        public string Work { get; }

        public TimeSpan Duration { get; }

        public Task<bool> RenewAsync(TimeSpan duration, CancellationToken ct = default) =>
            _owner.TryTakeAsync(Work, duration, ct);

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            try
            {
                await _owner.ReleaseAsync(Work, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // **الإفراجُ الفاشل ليس خطأً يُرمى**: العقدُ ينتهي بنفسه، فأسوأُ ما يقع أن ينتظر
                // العاملُ التالي دورةً واحدة. ورميُ استثناءٍ من `DisposeAsync` كان سيُخفي سببَ
                // الخروج الحقيقيّ من الكتلة التي تحته.
                _logger.LogWarning(ex, "Releasing lease {Work} failed; it will expire on its own", Work);
            }
        }
    }
}

// ============================================================================
// هويّةُ هذه النسخة من الخادم: رمزٌ يُولَّد عند الإقلاع ويعيش ما دامت العملية.
//
// **ولا يُشتقّ من اسم المضيف ولا من رقم العملية**: حاويتان بالاسم نفسه وارد، وإعادةُ تشغيلٍ
// تُعيد استعمال رقم عملية وارد كذلك — وتصادمُ هويّتين يعني نسختين تظنّان أنّهما حاملُ العقد
// نفسه، وهو أسوأُ من ألّا يكون هناك قفلٌ أصلاً.
// ============================================================================
public sealed class InstanceIdentity
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}
