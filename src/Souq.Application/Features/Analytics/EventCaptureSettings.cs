namespace Souq.Application.Features.Analytics;

// ============================================================================
// إعداد الالتقاط السلوكي — **معطّلٌ حتى يُضبَط، وهذا هو الشرط الذي أذن به قرار المالك** لا تحفّظاً
// هندسياً.
//
// قرار المالك C-08 = A (2026-09-21) أذن بتخزين معرّف زائر مُعتِم للمتسوّق غير المُسجَّل، ونصُّ
// الخيار يُبقي ثلاثة أجوبة للمالك **«قبل أن يُكتب أوّل صفّ»**: الأساس القانوني الذي يُعتمد عليه،
// ومدّة الحفظ المُعلَنة، وهل يُعالَج شيءٌ خارج الأردن. والطريقةُ الوحيدة لاحترام «قبل أن يُكتب
// أوّل صفّ» حرفياً هي أن يمنع غيابُ الجواب الكتابةَ نفسها.
//
// فالبناء كلّه يُشحَن وهو معطّل:
//   • `Enabled = false` ⇒ `IEventSink.Record` لا يكتب شيئاً ولا يعدّ إسقاطاً (لا شيء أُسقط).
//   • `RetentionDays = 0` ⇒ لا مدّة افتراضية. البحثُ وجد 13 شهراً (عمرُ مُتتبِّعٍ عند منظِّم) و14
//     شهراً (عُرف الصناعة)، **وكلاهما بحثٌ لا قرار**، فلا يُكتب أيٌّ منهما هنا افتراضاً.
//   • `LawfulBasis = ""` ⇒ لا أساس مفترَض.
//   • `VisitorIdentifierEnabled = false` ⇒ **ويبقى الالتقاط ممكناً بلا معرّف زائر**: مجاميعٌ لا
//     تحتاج ربطَ فعلَين بشخص. وهذا هو جواب C-08 = «لا» محفوظاً في الشكل نفسه، فالبناء يخدم
//     الجوابَين ولا يفترض أحدهما.
//
// وتفعيلُ الالتقاط بلا مدّةٍ أو بلا أساس **يمنع الإقلاع** برسالة تسمّي المفتاح و`C-08`: التشغيل
// نصفَ مضبوطٍ هو بالضبط الحادث الذي يجعل الجواب القانوني أثراً لا قراراً.
// ============================================================================
public sealed class EventCaptureSettings
{
    public const string SectionName = "Analytics:Events";

    // أدنى وأقصى ما يُقبَل كمدّة حفظٍ **إن** فُعِّل الالتقاط. لا افتراضيّ بينهما.
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 730;

    public bool Enabled { get; set; }

    public bool VisitorIdentifierEnabled { get; set; }

    public int RetentionDays { get; set; }

    // نصٌّ حرّ يكتبه المالك (مثلاً "consent" أو "legitimate-interest") ويظهر في سجلّ الإقلاع.
    // الكودُ لا يفسّره ولا يشتقّ منه سلوكاً — وجودُه وحده هو الشرط، لأنّ غيابَه يعني أنّ أحداً لم
    // يُجب. تفسيرُه قانونيّ، وليس لهذا الملفّ رأيٌ فيه.
    public string LawfulBasis { get; set; } = "";

    // خمولُ الجلسة: ثلاثون دقيقة، وهو العُرف الذي التقت عليه منصّتان مستقلّتان (ADR-0050 §4).
    // قابلٌ للضبط لأنّ التعريف تجاريّ، ومقطوعٌ عند الكتابة لأنّ استنباطَه لاحقاً ينكسر يوم يتغيّر.
    public int SessionIdleMinutes { get; set; } = 30;

    // نافذةُ تجميع الكتابة ومَسْحُ التجميع والمسح، كما في مسار سجلّ البحث.
    public int WriteBatchMilliseconds { get; set; } = 2000;
    public int RollupIntervalMinutes { get; set; } = 60;
    public int PurgeIntervalMinutes { get; set; } = 360;
    public int PurgeBatchSize { get; set; } = 5000;

    public TimeSpan WriteBatchWindow => TimeSpan.FromMilliseconds(WriteBatchMilliseconds);
    public TimeSpan SessionIdle => TimeSpan.FromMinutes(SessionIdleMinutes);

    // الالتقاط لا يعمل إلا مضبوطاً كاملاً. تُقرأ عند الإقلاع وفي المصرف معاً: الأولى تمنع نشراً
    // نصفَ مضبوط، والثانية تحرس الحالةَ التي يُبنى فيها المصرف بلا مرور بالتحقّق (اختبار).
    public bool CaptureIsConfigured =>
        Enabled
        && RetentionDays is >= MinRetentionDays and <= MaxRetentionDays
        && !string.IsNullOrWhiteSpace(LawfulBasis);
}
