using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Souq.Application.Common.Observability;

namespace Souq.Application.Tests.Observability;

// ============================================================================
// المقاييس تُصدر فعلاً (M17).
//
// اختبارٌ صغير لسببٍ محدّد: عدّادٌ لا يُصدر شيئاً لا يُلاحَظ أبداً. لا شاشة تعرضه، ولا اختبار يفشل بسببه،
// ولا أحد يكتشف غيابه إلا في اللحظة التي يُسأل فيها "كم مرّة حدث هذا؟" — وهي لحظة حادثة.
//
// ويُفحص أيضاً أنّ الوسوم تصل: عدّاد دخولٍ فاشل بلا وسم السبب يجمع النسيان والحشو في رقمٍ واحد، فيفقد
// كلَّ ما وُضع لأجله.
// ============================================================================
public class SouqMetricsTests
{
    private static (List<(string Name, long Value, string? Tag)> Measurements, MeterListener Listener) Listen()
    {
        var captured = new List<(string, long, string?)>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == SouqMetrics.MeterName) l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? tag = null;
            foreach (var t in tags) tag = t.Value?.ToString();
            captured.Add((instrument.Name, value, tag));
        });
        listener.Start();
        return (captured, listener);
    }

    [Fact]
    public void كل_عدّاد_يُصدر_قيمته()
    {
        var (measurements, listener) = Listen();
        using (listener)
        {
            SouqMetrics.RecordOutboxDeadLettered("PasswordResetRequested");
            SouqMetrics.RecordCrossTenantWriteBlocked();
            SouqMetrics.RecordLoginFailed("WrongPassword");
            SouqMetrics.RecordSearchLogDropped();
        }

        measurements.Select(m => m.Name).Should().BeEquivalentTo([
            "souq.outbox.dead_lettered",
            "souq.tenancy.cross_tenant_write_blocked",
            "souq.auth.login_failed",
            "souq.search.log_dropped",
        ]);
        measurements.Should().OnlyContain(m => m.Value == 1);
    }

    // الوسم هو ما يجعل العدّاد مفيداً: بلا السبب لا يُميَّز مستخدمٌ نسي كلمته من حملةِ حشو.
    [Fact]
    public void عدّاد_الدخول_الفاشل_موسومٌ_بسببه()
    {
        var (measurements, listener) = Listen();
        using (listener)
        {
            SouqMetrics.RecordLoginFailed("NoSuchAccount");
            SouqMetrics.RecordLoginFailed("AccountLocked");
        }

        measurements.Select(m => m.Tag).Should().BeEquivalentTo(["NoSuchAccount", "AccountLocked"]);
    }
}
