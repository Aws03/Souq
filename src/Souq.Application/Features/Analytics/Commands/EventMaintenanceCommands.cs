using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Features.Analytics.Contracts;

namespace Souq.Application.Features.Analytics.Commands;

// ============================================================================
// صيانةُ مخزن الأحداث لمتجرٍ واحد، بأمرَين ينفّذهما مَسْحان خلفيّان بالترتيب الملزِم:
// **تجميعٌ أوّلاً، ثم مسحٌ لا يتجاوز ما جُمِّع** ([ADR-0050](0050) §6).
// ============================================================================

// ── التجميع ───────────────────────────────────────────────────────────────

public record RollUpBehaviouralEventsCommand : IRequest<int>;

public class RollUpBehaviouralEventsHandler : IRequestHandler<RollUpBehaviouralEventsCommand, int>
{
    // سقفُ الأيام في الدورة الواحدة: متجرٌ توقّف تجميعه شهراً يلحق تدريجياً بدل أن يحبس المسح
    // كلّه في دورةٍ واحدة طويلة. والسقفُ يُسجَّل حين يُبلَغ، فلا يبقى التأخّر صامتاً.
    public const int MaxDaysPerCycle = 14;

    private readonly IEventRollups _rollups;
    private readonly TimeProvider _clock;
    private readonly ILogger<RollUpBehaviouralEventsHandler> _logger;

    public RollUpBehaviouralEventsHandler(
        IEventRollups rollups, TimeProvider clock, ILogger<RollUpBehaviouralEventsHandler> logger)
    {
        _rollups = rollups; _clock = clock; _logger = logger;
    }

    public async Task<int> Handle(RollUpBehaviouralEventsCommand command, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var written = 0;

        for (var day = 0; day < MaxDaysPerCycle; day++)
        {
            var next = await _rollups.NextDayToRollUpAsync(now, ct);
            if (next is null) return written;
            written += await _rollups.RollUpAsync(next.Value, now, ct);
        }

        _logger.LogWarning(
            "Behavioural event rollup stopped at its per-cycle ceiling of {MaxDays} days; more days are still waiting",
            MaxDaysPerCycle);
        return written;
    }
}

// ── المسح ─────────────────────────────────────────────────────────────────

public record PurgeBehaviouralEventsCommand : IRequest<int>;

// ============================================================================
// يمسح الصفوف الخام التي تجاوزت مدّة الحفظ — **وما جُمِّع منها وحده**.
//
// الحدُّ الفعليّ هو الأصغرُ من اثنين: انقضاءُ مدّة الحفظ، ونهايةُ ما جُمِّع. فمتجرٌ تعطّل تجميعه
// **يكبر سجلّه ولا يفقد تاريخه** — وهو المقايضة الصحيحة: القرص أرخص من مجموعٍ لا يمكن احتسابه
// مرّةً ثانية.
//
// والالتقاط معطّلاً لا يعني مسحاً معطّلاً: متجرٌ التُقط له أحداثٌ ثم أُوقف الالتقاط تبقى مدّةُ
// حفظِ ما التُقط سارية. ولذلك يقرأ هذا الأمرُ `RetentionDays` ولا يقرأ `Enabled`.
// ============================================================================
public class PurgeBehaviouralEventsHandler : IRequestHandler<PurgeBehaviouralEventsCommand, int>
{
    public const int MaxBatchesPerCycle = 200;

    private readonly IEventStoreRetention _retention;
    private readonly IEventRollups _rollups;
    private readonly EventCaptureSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<PurgeBehaviouralEventsHandler> _logger;

    public PurgeBehaviouralEventsHandler(
        IEventStoreRetention retention, IEventRollups rollups, EventCaptureSettings settings,
        TimeProvider clock, ILogger<PurgeBehaviouralEventsHandler> logger)
    {
        _retention = retention; _rollups = rollups; _settings = settings; _clock = clock; _logger = logger;
    }

    public async Task<int> Handle(PurgeBehaviouralEventsCommand command, CancellationToken ct)
    {
        // مدّةٌ غير مضبوطة ⇒ لا مسح. لا افتراضيّ يُخترع هنا: المدّة جوابُ المالك (C-08).
        if (_settings.RetentionDays is < EventCaptureSettings.MinRetentionDays
            or > EventCaptureSettings.MaxRetentionDays)
            return 0;

        var now = _clock.GetUtcNow().UtcDateTime;
        var retentionCutoff = now.AddDays(-_settings.RetentionDays);

        var rolledUpThrough = await _rollups.RolledUpThroughAsync(ct);
        if (rolledUpThrough is null)
        {
            _logger.LogInformation(
                "Behavioural event purge skipped: nothing has been rolled up yet, so no day may be purged");
            return 0;
        }

        // ما جُمِّع يشمل يومَ العلامة كاملاً، فالحدّ هو أوّلُ لحظةٍ من اليوم الذي يليه.
        var rollupLimit = rolledUpThrough.Value.Date.AddDays(1);
        var before = retentionCutoff < rollupLimit ? retentionCutoff : rollupLimit;

        if (before < rollupLimit && retentionCutoff > rollupLimit)
            _logger.LogWarning(
                "Behavioural event purge is held back by the rollup watermark {Watermark:yyyy-MM-dd}, not by retention",
                rolledUpThrough.Value);

        var deleted = 0;
        for (var batch = 0; batch < MaxBatchesPerCycle; batch++)
        {
            var removed = await _retention.PurgeBeforeAsync(before, _settings.PurgeBatchSize, ct);
            deleted += removed;
            if (removed < _settings.PurgeBatchSize) return deleted;
        }

        _logger.LogWarning(
            "Behavioural event purge stopped at its per-cycle ceiling after {Deleted} rows; older rows remain",
            deleted);
        return deleted;
    }
}
