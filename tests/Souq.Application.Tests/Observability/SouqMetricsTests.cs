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

    // ── عن عزل هذين الاختبارين (أُصلح في M18) ─────────────────────────────────
    // `Meter` عامٌّ على مستوى العملية، والمستمع يلتقط ما يُصدره **أي** اختبار يعمل بالتوازي —
    // و`AuthHandlersTests` يُشغّل `LoginHandler` الذي يُصدر `souq.auth.login_failed`. فكان
    // التأكيد على تساوي القائمة تماماً يفشل نحو مرّة كل ستّ تشغيلات، حين يسبق قياسٌ غريب قياساتنا.
    // (لم يظهر محلّياً؛ ظهر أوّل ما شُغِّلت البوّابة على Linux — scripts/ci-local.sh.)
    //
    // والعلاج ليس إضعاف التأكيد بل تصويبه إلى ما يمكن للاختبار إثباته فعلاً:
    //   • وسمٌ فريد لعدّاد الدخول ⇒ قياسات الاختبارات الأخرى تُستبعَد باسمها لا بتوقيتها.
    //   • `Distinct()` على الأسماء ⇒ «أيّ العدّادات أصدرت» يبقى تأكيداً دقيقاً، ولا يكسره
    //     قياسٌ مكرّر من اختبارٍ متوازٍ. وعدّادٌ توقّف عن الإصدار ما زال يُفشل الاختبار، وهو غرضه.
    private const string OutboxDeadLettered = "souq.outbox.dead_lettered";
    private const string CrossTenantBlocked = "souq.tenancy.cross_tenant_write_blocked";
    private const string LoginFailed = "souq.auth.login_failed";
    private const string SearchLogDropped = "souq.search.log_dropped";

    [Fact]
    public void كل_عدّاد_يُصدر_قيمته()
    {
        // قيمة لا يستعملها أي مسار إنتاج ولا اختبار آخر: تمييز قياساتنا عن قياسات المتوازين.
        const string outcome = "OnlyThisTest";

        var (measurements, listener) = Listen();
        using (listener)
        {
            SouqMetrics.RecordOutboxDeadLettered("PasswordResetRequested");
            SouqMetrics.RecordCrossTenantWriteBlocked();
            SouqMetrics.RecordLoginFailed(outcome);
            SouqMetrics.RecordSearchLogDropped();
        }

        var ours = measurements.Where(m => m.Name != LoginFailed || m.Tag == outcome).ToList();

        ours.Select(m => m.Name).Distinct()
            .Should().BeEquivalentTo([OutboxDeadLettered, CrossTenantBlocked, LoginFailed, SearchLogDropped]);
        ours.Should().OnlyContain(m => m.Value == 1);
    }

    // الوسم هو ما يجعل العدّاد مفيداً: بلا السبب لا يُميَّز مستخدمٌ نسي كلمته من حملةِ حشو.
    [Fact]
    public void عدّاد_الدخول_الفاشل_موسومٌ_بسببه()
    {
        const string forgot = "NoSuchAccount-OnlyThisTest";
        const string locked = "AccountLocked-OnlyThisTest";

        var (measurements, listener) = Listen();
        using (listener)
        {
            SouqMetrics.RecordLoginFailed(forgot);
            SouqMetrics.RecordLoginFailed(locked);
        }

        measurements.Where(m => m.Name == LoginFailed).Select(m => m.Tag)
            .Should().Contain([forgot, locked]);
    }
}
