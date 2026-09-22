using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Interfaces;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Coordination;

// ============================================================================
// نشرُ إشاراتِ الإبطال وقراءتُها (C4، [ADR-0057](0057)).
//
// **الزيادةُ جملةٌ واحدة ذرّية** — `SET Version = Version + 1` — لا قراءةٌ فزيادةٌ فكتابة:
// نسختان تُبطلان معاً بقراءةٍ ثمّ كتابة تكتبان القيمةَ نفسها، فتضيع إحدى القفزتين. وضياعُ قفزةٍ
// هنا يعني نسخةً ثالثة لا تعرف أنّ شيئاً تغيّر — وهو بالضبط التقادمُ الذي وُجدت الإشارةُ لتُنهيه.
//
// والنمطُ هو نمطُ `OrderNumbers` و`TenantQuotaGuard` نفسه، ولنفس السبب الذي تسوقه ADR-0049.
// ============================================================================
internal sealed class SqlCacheSignals : ICacheSignals
{
    private readonly AppDbContext _db;
    private readonly ILogger<SqlCacheSignals> _logger;

    public SqlCacheSignals(AppDbContext db, ILogger<SqlCacheSignals> logger)
    {
        _db = db; _logger = logger;
    }

    public async Task BumpAsync(string signal, CancellationToken ct = default)
    {
        var name = CacheSignal.For(signal).Name;

        if (await Increment(name, ct)) return;

        // لا صفَّ بعد: يُنشأ ثمّ تُعاد الزيادة. وسباقُ إنشاءَين يحسمه الفهرسُ الفريد، والنوعُ
        // المُصطاد `UniqueConstraintViolationException` لا `DbUpdateException` — المزلقُ نفسه
        // الذي وقع في `OrderNumbers`.
        var created = CacheSignal.For(name);
        _db.CacheSignals.Add(created);
        try
        {
            await _db.SaveChangesAsync(ct);
            return;   // صفٌّ جديد بجيلٍ 0: أيُّ نسخةٍ تقرؤه لأوّل مرّة تعتبره خطَّ الأساس
        }
        catch (UniqueConstraintViolationException)
        {
            _db.Entry(created).State = EntityState.Detached;
        }

        if (!await Increment(name, ct))
            _logger.LogWarning("Cache signal {Signal} could not be bumped; other instances stay stale until TTL", name);
    }

    // ============================================================================
    // كلُّ الإشارات في نداءٍ واحد. المراقبُ يسأل كلَّ دورة، فسؤالٌ لكلِّ ذاكرةٍ كان سيضاعف
    // الرحلات بلا سبب — والجدولُ صفّان اليوم.
    //
    // ولا `AsNoTracking` للتزيين: الصفوفُ لا تُعدَّل من هذا المسار أصلاً، لكنّ تتبّعها كان
    // سيُبقيها في سياقٍ يعيش طولَ الدورة بلا فائدة.
    // ============================================================================
    public async Task<IReadOnlyDictionary<string, long>> ReadAllAsync(CancellationToken ct = default) =>
        await _db.CacheSignals.AsNoTracking().ToDictionaryAsync(s => s.Name, s => s.Version, ct);

    private async Task<bool> Increment(string name, CancellationToken ct) =>
        await _db.CacheSignals
            .Where(s => s.Name == name)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
}
