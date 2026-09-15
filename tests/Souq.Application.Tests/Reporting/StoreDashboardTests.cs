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

    [Fact]
    public void اليوم_يبدأ_من_منتصف_ليلته_وينتهي_قبل_الغد()
    {
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Today, Now);

        window.From.Should().Be(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        window.To.Should().Be(new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void آخر_سبعة_أيام_سبعة_لا_ثمانية()
    {
        // الخطأ الشائع: AddDays(-7) مع نهاية الغد يعطي ثمانية أيام.
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Last7Days, Now);

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
        var window = GetStoreDashboardHandler.WindowFor(range, Now);

        (window.To - window.From).Should().Be(TimeSpan.FromDays(days));
    }

    [Fact]
    public void المدّة_السابقة_مساوية_في_الطول_وملاصقة()
    {
        // سهم المقارنة يقارن مثيلاً بمثيل، وإلا صار "+40%" أثراً لطول مختلف لا لنموّ.
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.Last30Days, Now);

        (window.From - window.PreviousFrom).Should().Be(window.To - window.From);
    }

    [Fact]
    public void هذه_السنة_تبدأ_من_أول_يناير()
    {
        var window = GetStoreDashboardHandler.WindowFor(ReportRange.ThisYear, Now);

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
        GetStoreDashboardHandler.WindowFor(range, Now).GroupByMonth.Should().Be(byMonth);
    }

    [Fact]
    public async Task المعالج_يمرّر_المدّة_المحسوبة_من_الساعة_المحقونة_لا_من_الطلب()
    {
        var reports = Substitute.For<IStoreReports>();
        var clock = new FixedClock(Now);
        reports.GetDashboardAsync(default, default!, default)
            .ReturnsForAnyArgs(Task.FromResult(Empty()));

        await new GetStoreDashboardHandler(reports, clock)
            .Handle(new GetStoreDashboardQuery(ReportRange.Last7Days), CancellationToken.None);

        await reports.Received(1).GetDashboardAsync(
            ReportRange.Last7Days,
            Arg.Is<ReportWindow>(w => w.From == new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc)),
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
