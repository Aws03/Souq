using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Reporting;
using Souq.Application.Tests.TestDoubles;

namespace Souq.Application.Tests.Reporting;

// ============================================================================
// حدود المدّة هي المكان الذي يصير فيه رقمٌ صحيح رقماً خاطئاً بهدوء: يومٌ زائد في طرف المدّة
// يغيّر "الإيراد هذا الأسبوع"، ومدّةٌ سابقة بطول مختلف تجعل سهم المقارنة يكذب.
//
// وهي أيضاً حدّ أمان: المدّة تُشتقّ من قائمة مغلقة وساعة محقونة، لا من تاريخين يرسلهما
// المتصفّح — فلا مدى يُوسَّع من العميل ليجرّ جدول الطلبات كلّه.
// ============================================================================
public class StoreDashboardWindowTests
{
    // لحظة داخل اليوم لا منتصف ليلته: الحدود يجب أن تُحسب من التاريخ لا من الوقت.
    private static readonly DateTime Now = new(2026, 3, 15, 13, 42, 7, DateTimeKind.Utc);

    // متجرٌ بمنطقة UTC: كل تأكيدات هذا الملف كانت مكتوبة له ضمناً، وهي تبقى صحيحة له حرفياً بعد
    // C11 — ما تغيّر أن المنطقة صارت **مُعطاةً** لا مفترضة. الحالات المحلّية في الصنف الذي يليه.

    [Fact]
    public void اليوم_يبدأ_من_منتصف_ليلته_وينتهي_قبل_الغد()
    {
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Today, Now, StoreTimeZone.Utc);

        window.From.Should().Be(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        window.To.Should().Be(new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void آخر_سبعة_أيام_سبعة_لا_ثمانية()
    {
        // الخطأ الشائع: AddDays(-7) مع نهاية الغد يعطي ثمانية أيام.
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Last7Days, Now, StoreTimeZone.Utc);

        (window.To - window.From).Should().Be(TimeSpan.FromDays(7));
        window.From.Should().Be(new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(ReportRange.Today, 1)]
    [InlineData(ReportRange.Last7Days, 7)]
    [InlineData(ReportRange.Last30Days, 30)]
    [InlineData(ReportRange.Last90Days, 90)]
    public void كل_مدّة_بطولها_المعلن(ReportRange range, int days)
    {
        var window = GetStoreDashboardHandler.WindowFor(range, Now, StoreTimeZone.Utc);

        (window.To - window.From).Should().Be(TimeSpan.FromDays(days));
    }

    [Fact]
    public void المدّة_السابقة_مساوية_في_الطول_وملاصقة()
    {
        // سهم المقارنة يقارن مثيلاً بمثيل، وإلا صار "+40%" أثراً لطول مختلف لا لنموّ.
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Last30Days, Now, StoreTimeZone.Utc);

        (window.From - window.PreviousFrom).Should().Be(window.To - window.From);
    }

    [Fact]
    public void هذه_السنة_تبدأ_من_أول_يناير()
    {
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.ThisYear, Now, StoreTimeZone.Utc);

        window.From.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        window.To.Should().Be(new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(ReportRange.Today, false)]
    [InlineData(ReportRange.Last7Days, false)]
    [InlineData(ReportRange.Last30Days, false)]
    [InlineData(ReportRange.Last90Days, true)]
    [InlineData(ReportRange.ThisYear, true)]
    public void المدد_الطويلة_وحدها_تُجمَّع_شهرياً(ReportRange range, bool byMonth)
    {
        // ٣٦٥ نقطة على منحنى عرضه ٦٠٠ بكسل ليست معلومة أكثر، وهي حمولة أكبر.
        GetStoreDashboardHandler.WindowFor(range, Now, StoreTimeZone.Utc).GroupByMonth.Should().Be(byMonth);
    }

    [Fact]
    public async Task المعالج_يمرّر_المدّة_المحسوبة_من_الساعة_المحقونة_ومن_منطقة_المتجر()
    {
        var reports = Substitute.For<IStoreReports>();
        var clock = new FixedClock(Now);
        reports.GetDashboardAsync(default, default!, default)
            .ReturnsForAnyArgs(Task.FromResult(Empty()));

        // متجر الاختبار في Asia/Amman، فالنافذة يجب أن تكون نافذته هو — لا نافذة UTC.
        await new GetStoreDashboardHandler(reports, TestTenant.Context(), clock)
            .Handle(new GetStoreDashboardQuery(ReportRange.Last7Days), CancellationToken.None);

        await reports.Received(1).GetDashboardAsync(
            ReportRange.Last7Days,
            // 9 آذار 00:00 بعمّان = 8 آذار 21:00 بـ UTC. لو قرأ المعالج الساعة وحدها لكانت 9 آذار 00:00 UTC.
            Arg.Is<ReportWindow>(w => w.From == new DateTime(2026, 3, 8, 21, 0, 0, DateTimeKind.Utc)
                                      && w.Zone.Id == "Asia/Amman"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void قراءة_اللوحة_تُدقَّق_بمدّتها()
    {
        // الوحدة تحت Features.Reporting، فالتدقيق مفروض باختبار معماري — وهو مقصود هنا:
        // قراءة أرقام أعمال المتجر حدثٌ يستحقّ التسجيل.
        var record = new GetStoreDashboardQuery(ReportRange.Last90Days).ToAuditRecord();

        record.Action.Should().Be("store.dashboard.viewed");
        record.Metadata!["range"].Should().Be("Last90Days");
    }

    private static StoreDashboardDto Empty() => new(
        "Last7Days", Now, Now, "JOD",
        new PeriodTotalsDto(0, 0, 0, 0, 0, 0), new PeriodTotalsDto(0, 0, 0, 0, 0, 0),
        [], new Dictionary<string, int>(), [], [],
        new InventorySnapshotDto(0, 0, 0), 0, 0, 0, 0);
}

// ============================================================================
// يوم المتجر لا يوم UTC (C11) — العيب الذي تصفه هذه الاختبارات صامت تماماً: الأرقام معقولة،
// وهي لمدّةٍ أخرى. تاجرٌ في عمّان يسأل عن "اليوم" فتبدأ نافذته الثالثة فجراً بتوقيته.
// ============================================================================
public class StoreDashboardLocalWindowTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 13, 42, 7, DateTimeKind.Utc);

    // عمّان = UTC+3 ثابتة (الأردن ألغى التوقيت الصيفي سنة 2022)، فالحساب فيها لا لبس فيه.
    private static StoreTimeZone Amman => StoreTimeZone.Resolve("Asia/Amman");
    private static StoreTimeZone NewYork => StoreTimeZone.Resolve("America/New_York");

    [Fact]
    public void اليوم_يبدأ_من_منتصف_ليل_المتجر_لا_منتصف_ليل_UTC()
    {
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Today, Now, Amman);

        // منتصف ليل 15 آذار بعمّان = 21:00 من 14 آذار بـ UTC.
        window.From.Should().Be(new DateTime(2026, 3, 14, 21, 0, 0, DateTimeKind.Utc));
        window.To.Should().Be(new DateTime(2026, 3, 15, 21, 0, 0, DateTimeKind.Utc));
        (window.To - window.From).Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void الإزاحة_السالبة_تُؤخّر_بداية_اليوم_لا_تُقدّمها()
    {
        // نيويورك في 15 آذار 2026 على التوقيت الصيفي (-4): منتصف ليلها = 04:00 UTC من اليوم نفسه.
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Today, Now, NewYork);

        window.From.Should().Be(new DateTime(2026, 3, 15, 4, 0, 0, DateTimeKind.Utc));
        window.To.Should().Be(new DateTime(2026, 3, 16, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void طلب_مساء_أمس_بتوقيت_المتجر_ليس_من_اليوم()
    {
        // ============================================================================
        // هذا هو العطل بعينه: طلبٌ عند 23:00 بعمّان من 14 آذار = 20:00 UTC من 14 آذار.
        // بحساب UTC كان يقع **خارج** نافذة "اليوم" (التي تبدأ 15 آذار 00:00 UTC) — صحيح صدفةً.
        // لكن طلباً عند 01:00 بعمّان من 15 آذار = 22:00 UTC من 14 آذار كان يقع خارجها **خطأً**:
        // هو من يوم التاجر وقد سقط من لوحته.
        // ============================================================================
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Today, Now, Amman);

        var oneAmLocalToday = new DateTime(2026, 3, 14, 22, 0, 0, DateTimeKind.Utc);
        var elevenPmLocalYesterday = new DateTime(2026, 3, 14, 20, 0, 0, DateTimeKind.Utc);

        oneAmLocalToday.Should().BeOnOrAfter(window.From).And.BeBefore(window.To,
            "الساعة الواحدة فجراً بتوقيت المتجر من يومه");
        elevenPmLocalYesterday.Should().BeBefore(window.From, "الحادية عشرة مساءً من أمسه ليست من يومه");
    }

    [Theory]
    [InlineData(ReportRange.Today, 1)]
    [InlineData(ReportRange.Last7Days, 7)]
    [InlineData(ReportRange.Last30Days, 30)]
    [InlineData(ReportRange.Last90Days, 90)]
    public void الطول_يبقى_بالأيّام_المحلّية_مهما_كانت_المنطقة(ReportRange range, int days)
    {
        GetStoreDashboardHandler.WindowFor(range, Now, Amman).Let(w => w.To - w.From)
            .Should().Be(TimeSpan.FromDays(days));
    }

    [Fact]
    public void المدّة_السابقة_تُطرح_بالأيّام_المحلّية_لا_بالتِّكّات()
    {
        // في منطقة بتوقيت صيفي، مدّة الثلاثين يوماً التي تسبق مباشرةً قد تكون ٧١٩ أو ٧٢١ ساعة.
        // المطلوب أن تكون **ثلاثين يوماً محلّياً** في الحالتين، فتُقارَن المدّة بمثيلتها.
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Last30Days, Now, NewYork);

        var zone = NewYork;
        zone.ToLocal(window.From).Date.AddDays(-30).Should().Be(zone.ToLocal(window.PreviousFrom).Date);
    }

    [Fact]
    public void هذه_السنة_تبدأ_من_أول_يناير_بتوقيت_المتجر()
    {
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.ThisYear, Now, Amman);

        window.From.Should().Be(new DateTime(2025, 12, 31, 21, 0, 0, DateTimeKind.Utc));
        Amman.ToLocal(window.From).Should().Be(new DateTime(2026, 1, 1, 0, 0, 0));
    }

    // ========================================================================
    // معرّف لا يُحلّ ⇒ UTC ولا استثناء. `Tenant.SetLocale` يتحقّق من **الشكل** وحده ويقول في
    // تعليقه إنه لا يسأل قاعدة مناطق النظام — فهذه الحالة ممكنة ببيانات صحيحة تماماً.
    // ========================================================================
    [Fact]
    public void منطقة_لا_تُحلّ_تعود_إلى_UTC_ولا_تُسقط_اللوحة()
    {
        var unknown = StoreTimeZone.Resolve("Mars/Olympus_Mons");

        unknown.Resolved.Should().BeFalse("الجواب يقول إنه عن UTC لا عن المنطقة المطلوبة");
        unknown.Zone.Should().Be(TimeZoneInfo.Utc);
        GetStoreDashboardHandler.WindowFor(ReportRange.Today, Now, unknown).From
            .Should().Be(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));

        StoreTimeZone.Resolve(null).Resolved.Should().BeTrue("الغياب ليس خطأً — المتجر بلا منطقة هو UTC");
    }

    // ========================================================================
    // منتصف ليلٍ **غير موجود**: بعض المناطق تقدّم ساعتها عند منتصف الليل تماماً، فلا وجود
    // لـ 00:00 ذلك اليوم. التحويل الساذج يرمي، واللوحة تسقط مرّة في السنة لسبب لا يخطر لأحد.
    // ========================================================================
    [Fact]
    public void منتصف_ليل_لا_وجود_له_يتقدّم_ولا_يرمي()
    {
        // سانتياغو (تشيلي) تقدّم ساعتها من 00:00 إلى 01:00 في ليلة التغيير.
        var santiago = StoreTimeZone.Resolve("America/Santiago");
        if (!santiago.Resolved) return;   // نظامٌ بلا قاعدة مناطق كاملة: لا شيء يُختبَر

        var act = () => GetStoreDashboardHandler.WindowFor(
            ReportRange.Last30Days, new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc), santiago);

        act.Should().NotThrow();
    }
}

internal static class LetExtensions
{
    // قراءةً فقط: يُبقي التأكيد في سطر واحد بلا متغيّر وسيط لكل حالة.
    public static TOut Let<TIn, TOut>(this TIn value, Func<TIn, TOut> map) => map(value);
}
