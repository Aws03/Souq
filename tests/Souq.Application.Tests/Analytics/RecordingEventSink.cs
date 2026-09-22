using Souq.Application.Features.Analytics.Contracts;

namespace Souq.Application.Tests.Analytics;

// ============================================================================
// منفذُ الأحداث، مسجِّلاً.
//
// **الافتراضُ معطَّل**، وهو ما يجعل إدخاله إلى حالات استخدامٍ قائمة بلا أثر على اختباراتها:
// المُنادون يقرؤون `Enabled` قبل أن يبنوا حمولة، فالمعطَّلُ يعني «لا شيء تغيّر» حرفياً — وهو
// السلوكُ نفسه الذي يراه متجرٌ لم يُفعِّل الالتقاط.
//
// ومَن يقصد الالتقاط يُشغّله ويقرأ `Recorded`.
// ============================================================================
internal sealed class RecordingEventSink : IEventSink
{
    public bool Enabled { get; init; }

    public List<(string Name, object Payload, Guid? SearchExecutionId)> Recorded { get; } = [];

    public void Record(string name, object payload, Guid? searchExecutionId = null) =>
        Recorded.Add((name, payload, searchExecutionId));
}
