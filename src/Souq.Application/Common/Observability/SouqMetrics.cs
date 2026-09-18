using System.Diagnostics.Metrics;

namespace Souq.Application.Common.Observability;

// ============================================================================
// عدّادات ما يُنذَر عنه (M17) — بـ `System.Diagnostics.Metrics` وحده، بلا أي تبعية جديدة.
//
// **لماذا مقاييس وقد كانت هناك سجلّات؟** لأنّ السجلّ يجيب "ماذا جرى في هذا الطلب" والمقياس يجيب "كم مرّة
// جرى هذا في الساعة الماضية". والثاني هو ما يُبنى عليه إنذار: لا أحد يُنذَر بسطر، بل بمعدّلٍ تجاوز حدّاً.
// وقد كتب M15 الأسطر (محاولات الدخول الفاشلة بأسبابها)؛ وهذه تجعلها قابلة للتجميع بلا قراءة نصّ.
//
// **ولماذا بلا OpenTelemetry؟** ADR-0018 اختار السجلّ المهيكل ومعرّف التتبّع W3C، وأجّل OTel لأنّها "تحتاج
// مُجمِّعاً وخلفية لتكون مفيدة — قرار تشغيل". وهذا ما زال صحيحاً: حزمة تصدير بلا وجهةٍ تُصدِّر إليها تبعيةٌ
// لا تفعل شيئاً. أمّا `Meter` فهي **في وقت تشغيل .NET نفسه**: تُصدر القيم دائماً، ولا يلزمها شيء ليُقرأ
// منها — تلتقطها `dotnet-counters` اليوم، ويلتقطها أي مُصدِّر يُضاف غداً بسطر إعداد لا بتغيير شيفرة.
//
// فالانقسام مقصود: **ما يُقاس** قرارُ هندسة يُتَّخذ هنا، **وأين يُرسَل** قرارُ نشرٍ لا يملكه هذا المستودع.
//
// وموضعها في Application لا في Infrastructure لنفس سبب `ILogger`: القياس شأنٌ عرضيّ تستعمله حالات
// الاستخدام نفسها (محاولة الدخول الفاشلة تُعَدّ حيث تُقرَّر)، و`System.Diagnostics.Metrics` مكتبةُ
// وقت تشغيلٍ لا بنيةٌ تحتية — فلا منفذ يُخترع لشيءٍ لا بديل له.
//
// والمختار قليل عمداً. مقياسٌ لكل شيء يُنتج لوحةً لا يقرأها أحد؛ هذه الأربعة كلٌّ منها **سؤالٌ يوقظ
// إنساناً**، ولكلٍّ منها اليوم سطرٌ في السجلّ لا يُجمَّع:
//   • رسالة صادر ماتت    ⇒ بريدٌ لن يصل أبداً: تأكيد طلب، أو رابط إعادة تعيين.
//   • كتابة عبر متجرين مُنعت ⇒ إمّا عيبٌ في العزل وإمّا محاولة. كلاهما يوقظ.
//   • دخولٌ فاشل بسببه    ⇒ المعدّل هو الفرق بين النسيان وحشو بيانات الاعتماد.
//   • سطر بحثٍ أُسقط     ⇒ القناة امتلأت: قياسٌ يُفقد بصمت ما لم يُعَدّ.
// ============================================================================
public static class SouqMetrics
{
    public const string MeterName = "Souq";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // رسالة في صندوق الصادر استُنفدت محاولاتها: لن تُسلَّم أبداً بلا تدخّل.
    private static readonly Counter<long> OutboxDeadLettered =
        Meter.CreateCounter<long>("souq.outbox.dead_lettered", "messages",
            "Outbox messages that exhausted their retries and will never be delivered");

    // كتابة رُفضت لأنّها تخصّ متجراً آخر — الحارس يعمل، والسؤال لماذا وصلت أصلاً.
    private static readonly Counter<long> CrossTenantWriteBlocked =
        Meter.CreateCounter<long>("souq.tenancy.cross_tenant_write_blocked", "attempts",
            "Writes refused because the row belongs to another store");

    // محاولة دخول فاشلة، موسومة بسببها — المعدّل يميّز النسيان من الحشو.
    private static readonly Counter<long> LoginFailed =
        Meter.CreateCounter<long>("souq.auth.login_failed", "attempts",
            "Failed sign-in attempts, tagged with the outcome");

    // سطر بحث لم يُسجَّل لأنّ الذاكرة المحدودة امتلأت.
    private static readonly Counter<long> SearchLogDropped =
        Meter.CreateCounter<long>("souq.search.log_dropped", "entries",
            "Search log entries dropped because the in-memory buffer was full");

    public static void RecordOutboxDeadLettered(string messageType) =>
        OutboxDeadLettered.Add(1, new KeyValuePair<string, object?>("message_type", messageType));

    // بلا وسمٍ بمعرّف المتجر: الوسم عالي التعدّد يُفجّر عدد السلاسل الزمنية في أي خلفية مقاييس، ومتجرٌ
    // بعينه يُعرَف من السجلّ الحَرِج الذي يُكتب في اللحظة نفسها. المقياس للإنذار، والسجلّ للتحقيق.
    public static void RecordCrossTenantWriteBlocked() => CrossTenantWriteBlocked.Add(1);

    public static void RecordLoginFailed(string outcome) =>
        LoginFailed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordSearchLogDropped() => SearchLogDropped.Add(1);
}
