using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Features.Products;
using Souq.Application.Features.Products.Commands;
using Souq.Application.Features.Products.Contracts;

namespace Souq.Application.Tests.Products;

// ============================================================================
// سياسة حفظ سجلّ البحث تُنجَز، لا تُبدأ (M15 — تصحيحُ عيبٍ في M13 نفسه).
//
// شحن M13 الأمر يحذف **دفعةً واحدة** لكل دورة، والمنسّق يُرسله كل ستّ ساعات: أي عشرون ألف صفٍّ في
// اليوم كحدٍّ أقصى للمتجر. ومسار الكتابة — `GET /api/products?keyword=` — نقطةٌ عامّة بلا تسجيل دخول
// ولا حدّ معدّل، تكتب صفّاً لكل بحث عبر قناةٍ تمرّر مئاتٍ في الثانية.
//
// فالمُدخَل يسبق المُخرَج بمراتب، ومعنى ذلك أنّ "تسعون يوماً" التي وثّقها M13 بوصفها إنجاز المرحلة
// **لم تكن مضمونة تحت أي حمل حقيقي**: الجدول ينمو، والصفوف الأقدم من المدّة لا يُبلَغ إليها أبداً.
//
// والاختبار هنا على المنطق لا على القاعدة: "هل تُكرَّر الدفعات حتى تفرغ؟" سؤالُ تطبيقٍ خالص، ويُجاب
// بمنفذٍ بديل في مللي ثوانٍ — بينما إثباته في SQL يحتاج آلاف الصفوف ودقائق.
// ============================================================================
public class PurgeSearchLogHandlerTests
{
    private readonly ISearchLogRetention _retention = Substitute.For<ISearchLogRetention>();
    private readonly SearchLogSettings _settings = new() { RetentionDays = 90, PurgeBatchSize = 100 };

    private PurgeSearchLogHandler Handler() =>
        new(_retention, _settings, TimeProvider.System, NullLogger<PurgeSearchLogHandler>.Instance);

    // دفعتان ممتلئتان ثمّ ناقصة: الدورة تمضي حتى الناقصة وتتوقّف عندها.
    [Fact]
    public async Task يُكرّر_الدفعات_حتى_تعود_واحدةٌ_ناقصة()
    {
        _retention.PurgeBeforeAsync(Arg.Any<DateTime>(), 100, Arg.Any<CancellationToken>())
            .Returns(100, 100, 40);

        var deleted = await Handler().Handle(new PurgeSearchLogCommand(), CancellationToken.None);

        deleted.Should().Be(240, "الدفعات الثلاث مجتمعةً — لا الأولى وحدها");
        await _retention.Received(3).PurgeBeforeAsync(Arg.Any<DateTime>(), 100, Arg.Any<CancellationToken>());
    }

    // لا شيء قديم ⇒ استعلامٌ واحد ولا تكرار: المسح لا يُكلّف شيئاً على متجرٍ نظيف.
    [Fact]
    public async Task لا_شيء_قديم_يعني_دفعةً_واحدة_فارغة()
    {
        _retention.PurgeBeforeAsync(Arg.Any<DateTime>(), 100, Arg.Any<CancellationToken>()).Returns(0);

        (await Handler().Handle(new PurgeSearchLogCommand(), CancellationToken.None)).Should().Be(0);
        await _retention.Received(1).PurgeBeforeAsync(Arg.Any<DateTime>(), 100, Arg.Any<CancellationToken>());
    }

    // ========================================================================
    // والسقف موجود: جدولٌ متروكٌ منذ شهور لا يجوز أن يحجز المنسّق ساعاتٍ في دورةٍ واحدة ويمنع بقيّة
    // المتاجر من دورها. ما يبقى يُحذف في الدورة التالية — وهذا وحده الفرق بين "بطيء" و"لا ينتهي".
    // ========================================================================
    [Fact]
    public async Task لا_يدور_بلا_نهاية_على_جدولٍ_ضخم()
    {
        // منفذٌ لا يفرغ أبداً: كل دفعة ممتلئة.
        _retention.PurgeBeforeAsync(Arg.Any<DateTime>(), 100, Arg.Any<CancellationToken>()).Returns(100);

        var deleted = await Handler().Handle(new PurgeSearchLogCommand(), CancellationToken.None);

        deleted.Should().Be(PurgeSearchLogHandler.MaxBatchesPerCycle * 100);
        await _retention.Received(PurgeSearchLogHandler.MaxBatchesPerCycle)
            .PurgeBeforeAsync(Arg.Any<DateTime>(), 100, Arg.Any<CancellationToken>());
    }

    // الحدّ الزمني يُحسب من مدّة الحفظ، لا من رقمٍ في الكود.
    [Fact]
    public async Task الحدّ_الزمني_من_مدّة_الحفظ_المُعدَّة()
    {
        _settings.RetentionDays = 30;
        _retention.PurgeBeforeAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(0);

        await Handler().Handle(new PurgeSearchLogCommand(), CancellationToken.None);

        await _retention.Received(1).PurgeBeforeAsync(
            Arg.Is<DateTime>(d => d < DateTime.UtcNow.AddDays(-29) && d > DateTime.UtcNow.AddDays(-31)),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
