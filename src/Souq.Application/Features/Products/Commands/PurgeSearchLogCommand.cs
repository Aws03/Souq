using MediatR;
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

    public PurgeSearchLogHandler(ISearchLogRetention retention, SearchLogSettings settings, TimeProvider clock)
    {
        _retention = retention; _settings = settings; _clock = clock;
    }

    public Task<int> Handle(PurgeSearchLogCommand cmd, CancellationToken ct) =>
        _retention.PurgeBeforeAsync(
            _clock.GetUtcNow().UtcDateTime.AddDays(-_settings.RetentionDays), _settings.PurgeBatchSize, ct);
}
