using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Observability;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Analytics;
using Souq.Application.Features.Analytics.Contracts;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// مسارُ كتابة الأحداث السلوكية: قناةٌ محدودة تُسقِط عند الامتلاء، ومُلتقِطٌ لا يرمي، وكاتبٌ خلفيّ
// يكتب دفعةً داخل نطاق كل متجر ([ADR-0050](0050) §1).
//
// النمطُ هو نمطُ `SearchLogBuffer` نفسه، **ومعه عطبان فيه يُصلَحان هنا لا يُورَثان**:
//   • المُلتقِط القديم يعود صامتاً في نطاق المنصّة، فحدثٌ من لوحةِ منصّةٍ يُفقَد بلا عدٍّ. هنا
//     الإسقاطُ يُعَدّ دائماً مهما كان سببه.
//   • الكاتبُ القديم يلفّ كلَّ الدورة بـ try/catch واحد، فعطبُ دفعةِ متجرٍ واحد يُسقِط دفعات
//     **كل** المتاجر الباقية في تلك الدورة. هنا لكل متجرٍ حمايتُه، كما في `StoreSweepService`.
// ============================================================================
internal sealed class EventChannel
{
    // أكبر من قناة سجلّ البحث بعشرة أضعاف: الأحداث السلوكية أكثر بمرتبة (ظهورُ كل عنصر في كل
    // قائمة)، وقناةٌ بحجم ألفٍ تُسقِط في ذروةٍ عادية.
    public const int Capacity = 10_000;

    private readonly Channel<BufferedEvent> _channel = Channel.CreateBounded<BufferedEvent>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<BufferedEvent> Reader => _channel.Reader;

    public bool TryWrite(BufferedEvent entry) => _channel.Writer.TryWrite(entry);
}

// معرّف المتجر بجانب الكيان لا داخله: الكيانات لا تملك ضابطاً لـ `TenantId` — يختمه
// `TenantWriteGuardInterceptor` عند الحفظ داخل نطاق المتجر.
internal sealed record BufferedEvent(int TenantId, BehaviouralEvent Entry);

// ============================================================================
// المُلتقِط. **لا يرمي أبداً، ولا يكتب شيئاً ما لم يكن الالتقاط مضبوطاً كاملاً.**
//
// وترتيبُ الفحوص مقصود: `Enabled` أوّلاً كي لا تُبنى حمولةٌ ولا تُسلسَل في التركيب المعطّل —
// فالإسقاط هناك ليس فقداً، إذ لم يُطلب التقاطُ شيء.
// ============================================================================
internal sealed class EventBuffer : IEventSink
{
    private static int _dropped;

    private readonly EventChannel _channel;
    private readonly EventCaptureSettings _settings;
    private readonly ITenantContext _tenant;
    private readonly IVisitorContext _visitor;
    private readonly TimeProvider _clock;
    private readonly ILogger<EventBuffer> _logger;

    public EventBuffer(
        EventChannel channel, EventCaptureSettings settings, ITenantContext tenant, IVisitorContext visitor,
        TimeProvider clock, ILogger<EventBuffer> logger)
    {
        _channel = channel; _settings = settings; _tenant = tenant; _visitor = visitor;
        _clock = clock; _logger = logger;
    }

    // مضبوطاً كاملاً **وفي نطاق متجر**: الحدث السلوكي ملكُ متجرٍ بالتعريف، فلا حدث بلا متجر.
    public bool Enabled => _settings.CaptureIsConfigured && _tenant.Scope == TenantScope.Tenant;

    public void Record(string name, object payload, Guid? searchExecutionId = null)
    {
        if (!Enabled) return;

        try
        {
            // حمولةٌ غير مسجَّلة أو اسمٌ لا يعرفه السجلّ ⇒ إسقاطٌ **يُعَدّ**: خطأُ برمجةٍ يجب أن
            // يظهر في عدّادٍ لا أن يختفي.
            if (BehaviouralEventPayloads.VersionFor(name, payload) is not int version)
            {
                Drop($"unregistered payload for {name}");
                return;
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var entry = BehaviouralEvent.For(
                eventId: Guid.CreateVersion7(),
                name: name,
                schemaVersion: version,
                surface: _visitor.Surface,
                culture: _tenant.Tenant!.DefaultCulture,
                payload: BehaviouralEventPayloads.Serialize(payload),
                occurredAt: now,
                receivedAt: now,
                // معرّف الزائر يُقرأ من السياق، وهو null إن كان المعرّف نفسه معطّلاً — فالمجاميع
                // تعمل بلا نسبةٍ إلى شخص، وهو جواب C-08 = «لا» في الشكل نفسه.
                visitorId: _settings.VisitorIdentifierEnabled ? _visitor.VisitorId : null,
                sessionId: _settings.VisitorIdentifierEnabled ? _visitor.SessionId : null,
                searchExecutionId: searchExecutionId ?? _visitor.SearchExecutionId,
                correlationId: _visitor.CorrelationId);

            if (entry is null)
            {
                Drop($"rejected envelope for {name}");
                return;
            }

            if (!_channel.TryWrite(new BufferedEvent(_tenant.Tenant!.Id, entry)))
                Drop("channel full");
        }
        catch (Exception ex)
        {
            // آخرُ خطٍّ: مهما حدث، لا يخرج استثناء من هذا المسار إلى صفحة متسوّق.
            _logger.LogWarning(ex, "Behavioural event discarded ({EventName})", name);
            SouqMetrics.RecordBehaviouralEventDropped();
        }
    }

    // سطرٌ واحد لكل مئة إسقاط، كما في سجلّ البحث: سطرٌ لكل إسقاطٍ في ذروةٍ يغرق السجلّ، ولا سطرَ
    // أبداً يعني فقداً صامتاً.
    private void Drop(string reason)
    {
        SouqMetrics.RecordBehaviouralEventDropped();
        if (Interlocked.Increment(ref _dropped) % 100 == 1)
            _logger.LogWarning("Behavioural events are being dropped ({DropReason}); {DropCount} so far", reason, _dropped);
    }
}
