using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Features.Analytics;
using Souq.Application.Features.Analytics.Commands;
using Souq.Application.Features.Analytics.Contracts;

namespace Souq.Application.Tests.Analytics;

// ============================================================================
// **«التجميع قبل المسح» قاعدةٌ يفرضها هذا المعالج** (ADR-0050 §6) — وهي أهمّ ما يُختبر في هذا
// المسار، لأنّ كسرَها لا يُكتشف: يُمسح يومٌ لم يُجمَّع، فيَضيع مجموعُه **إلى الأبد** ولا يحمرّ شيء
// ولا يشكو أحد، إلى أن يُسأل عن رقمٍ لم يبقَ ما يُحتسب منه.
//
// والحدُّ الفعليّ هو الأصغرُ من اثنين: انقضاءُ مدّة الحفظ، ونهايةُ ما جُمِّع. فمتجرٌ تعطّل تجميعه
// يكبر سجلّه ولا يفقد تاريخه — والقرص أرخص من مجموعٍ لا يُحتسب مرّتين.
// ============================================================================
public class PurgeBehaviouralEventsHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly IEventStoreRetention _retention = Substitute.For<IEventStoreRetention>();
    private readonly IEventRollups _rollups = Substitute.For<IEventRollups>();

    private PurgeBehaviouralEventsHandler Handler(EventCaptureSettings settings) => new(
        _retention, _rollups, settings, new FakeTimeProvider(Now),
        NullLogger<PurgeBehaviouralEventsHandler>.Instance);

    private static EventCaptureSettings Settings(int retentionDays = 30) => new()
    {
        Enabled = true, RetentionDays = retentionDays, LawfulBasis = "consent", PurgeBatchSize = 1000,
    };

    [Fact]
    public async Task لا_يُمسح_شيء_قبل_أول_تجميع()
    {
        _rollups.RolledUpThroughAsync(Arg.Any<CancellationToken>()).Returns((DateTime?)null);

        var deleted = await Handler(Settings()).Handle(new PurgeBehaviouralEventsCommand(), CancellationToken.None);

        deleted.Should().Be(0);
        await _retention.DidNotReceiveWithAnyArgs().PurgeBeforeAsync(default, default, default);
    }

    // مدّةٌ غير مضبوطة ⇒ لا مسح. لا تُخترَع مدّةٌ هنا: هي جواب المالك (C-08).
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(EventCaptureSettings.MaxRetentionDays + 1)]
    public async Task مدّة_حفظ_غير_مضبوطة_لا_تمسح_شيئاً(int days)
    {
        _rollups.RolledUpThroughAsync(Arg.Any<CancellationToken>()).Returns(Now.AddDays(-1));

        var deleted = await Handler(Settings(days)).Handle(new PurgeBehaviouralEventsCommand(), CancellationToken.None);

        deleted.Should().Be(0);
        await _retention.DidNotReceiveWithAnyArgs().PurgeBeforeAsync(default, default, default);
    }

    // الحالة المعتادة: التجميع متقدّم على مدّة الحفظ، فالحدُّ هو المدّة.
    [Fact]
    public async Task التجميع_متقدّماً_يمسح_إلى_حدّ_مدّة_الحفظ()
    {
        _rollups.RolledUpThroughAsync(Arg.Any<CancellationToken>()).Returns(Now.AddDays(-1));
        _retention.PurgeBeforeAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(7);

        var deleted = await Handler(Settings(30)).Handle(new PurgeBehaviouralEventsCommand(), CancellationToken.None);

        deleted.Should().Be(7);
        await _retention.Received(1).PurgeBeforeAsync(Now.AddDays(-30), 1000, Arg.Any<CancellationToken>());
    }

    // ============================================================================
    // الحالة التي يوجد الحرس لأجلها: التجميع **متأخّر** عن مدّة الحفظ. الحدّ يصير نهايةَ ما
    // جُمِّع، لا انقضاءَ المدّة — فلا يُمسح يومٌ لم يُحتسب مجموعُه.
    // ============================================================================
    [Fact]
    public async Task التجميع_متأخّراً_يحبس_المسح_عند_علامته_لا_عند_المدّة()
    {
        var watermark = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);   // متأخّر بأسبوعين
        _rollups.RolledUpThroughAsync(Arg.Any<CancellationToken>()).Returns(watermark);
        _retention.PurgeBeforeAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(3);

        await Handler(Settings(1)).Handle(new PurgeBehaviouralEventsCommand(), CancellationToken.None);

        // يومُ العلامة نفسه مُجمَّعٌ كاملاً، فالحدّ أوّلُ لحظةٍ من اليوم الذي يليه.
        await _retention.Received(1).PurgeBeforeAsync(
            watermark.AddDays(1), 1000, Arg.Any<CancellationToken>());
    }

    // دفعةٌ كاملة تعني أنّ ما بعدها موجود، فتُعاد — حتى دفعةٍ ناقصة أو سقفِ الدورة.
    [Fact]
    public async Task يُعيد_الدفعات_حتى_تنقص_واحدة()
    {
        _rollups.RolledUpThroughAsync(Arg.Any<CancellationToken>()).Returns(Now.AddDays(-1));
        _retention.PurgeBeforeAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(1000, 1000, 250);

        var deleted = await Handler(Settings()).Handle(new PurgeBehaviouralEventsCommand(), CancellationToken.None);

        deleted.Should().Be(2250);
        await _retention.Received(3).PurgeBeforeAsync(Arg.Any<DateTime>(), 1000, Arg.Any<CancellationToken>());
    }
}

// ساعةٌ ثابتة: المدّة تُحتسب من «الآن»، فاختبارُها يحتاج الآن معلوماً.
internal sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
