using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Features.Products.Contracts;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// تطبيق سياسة الحفظ على سجلّ البحث لمتجر السياق (M13) — يُرسله منسّقٌ دوري، ويُرسله الاختبار مباشرةً.
//
// **دفعةٌ محدودة لا "كل القديم"**: نفس قاعدة `PurgeExpiredBasketsCommand` — معاملةٌ صغيرة على جدولٍ يُكتب
// فيه باستمرار أفضل من واحدةٍ كبيرة تُقفل صفحاته. والباقي يُحذف في الدورة التالية.
//
// **ولا صفوفَ تُحمَّل إلى الذاكرة**: الحذف هنا `DELETE WHERE` بالصيغة المجمَّعة (`ISearchLogRetention`)، لأنّ
// دفعةً من خمسة آلاف صفٍّ تُقرأ ثم تُتبَّع ثم تُحذف تكلّف أضعاف ما تكلّفه جملةٌ واحدة — وهذا الجدولُ أكبر
// جدولٍ في النظام حجماً. ولا قاعدةَ عملٍ في الحذف تحتاج كياناً محمَّلاً: السطر بيانٌ لا تجمّع.
// ============================================================================
public record PurgeSearchLogCommand : IRequest<int>;

public class PurgeSearchLogHandler : IRequestHandler<PurgeSearchLogCommand, int>
{
    private readonly ISearchLogRetention _retention;
    private readonly SearchLogSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<PurgeSearchLogHandler> _logger;

    public PurgeSearchLogHandler(
        ISearchLogRetention retention, SearchLogSettings settings, TimeProvider clock,
        ILogger<PurgeSearchLogHandler> logger)
    {
        _retention = retention; _settings = settings; _clock = clock; _logger = logger;
    }

    // ============================================================================
    // يُكرَّر حتى يفرغ، لا دفعةً واحدة (M15 — تصحيحُ عيبٍ في M13 نفسه).
    //
    // كان الأمر يحذف دفعةً واحدة، والمنسّق يُرسله مرّةً كل ستّ ساعات: أي **عشرين ألف صفّ في اليوم**
    // للمتجر كحدٍّ أقصى. ومسار الكتابة نقطةٌ عامّة بلا تسجيل دخول ولا حدّ معدّل، تكتب صفّاً لكل بحث،
    // وقناتها تمرّر نحو خمسمئة صفٍّ في الثانية. فالمُدخَل يسبق المُخرَج بمراتب، والجدول ينمو بلا حدّ
    // مهما طالت المدّة — أي أنّ "تسعون يوماً" التي وثّقها M13 **لم تكن مضمونة أصلاً**، وسياسة الحفظ
    // التي كانت كلّ فخر تلك المرحلة كانت اسماً بلا أثر تحت أي حمل حقيقي.
    //
    // وحجم الدفعة يبقى كما هو ولنفس سببه: معاملةٌ صغيرة على جدولٍ يُكتب فيه في اللحظة نفسها. المتغيّر
    // هو أنّ الدورة تُكرّر الدفعات حتى تعود واحدةٌ ناقصة — فالصِّغَر يبقى، ويُنجَز العمل.
    //
    // وسقفٌ للدورة رغم ذلك: جدولٌ متروكٌ منذ شهور لا يجوز أن يحجز المنسّق ساعاتٍ في أول تشغيل ويمنع
    // بقيّة المتاجر من دورها. ما يبقى يُحذف في الدورة التالية، والسقف يُسجَّل كي لا يكون التأخّر صامتاً.
    // ============================================================================
    public const int MaxBatchesPerCycle = 200;

    public async Task<int> Handle(PurgeSearchLogCommand cmd, CancellationToken ct)
    {
        var before = _clock.GetUtcNow().UtcDateTime.AddDays(-_settings.RetentionDays);
        var total = 0;

        for (var batch = 0; batch < MaxBatchesPerCycle; batch++)
        {
            var deleted = await _retention.PurgeBeforeAsync(before, _settings.PurgeBatchSize, ct);
            total += deleted;
            // دفعةٌ ناقصة ⇒ لم يبقَ ما هو أقدم من المدّة.
            if (deleted < _settings.PurgeBatchSize) return total;
        }

        _logger.LogWarning(
            "Search log purge hit its per-cycle ceiling for this store ({Deleted} rows); the rest waits for the next cycle",
            total);
        return total;
    }
}
