using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Products;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// الكاتب الخلفي لسجلّ البحث (M13): يُفرغ القناة على دفعات، كلّ متجرٍ داخل نطاقه.
//
// **ليس `StoreSweepService`** رغم قرابته: تلك تمرّ على **كلّ** متجر كلّ دورة وتسأله عملاً. هذه تنتظر
// وصول عمل، ولا تلمس إلا المتاجر التي بُحث فيها فعلاً. متجرٌ نائم لا يُكلِّف شيئاً.
//
// **والدفعة تُجمَع بالمتجر ثم تُحفظ في نطاقه**، لأنّ `TenantWriteGuardInterceptor` يختم `TenantId` من
// سياق المستأجر: الحفظُ خارج نطاقٍ يعني صفّاً بلا متجر، والحفظُ في نطاق الخطأ يعني سجلّ متجرٍ في
// متجرٍ آخر. فالنطاق ليس تفصيلاً هنا بل هو ما يجعل الصفّ صحيحاً.
//
// **وفشل دفعةٍ يُسجَّل ويُنسى**: السجلّ إحصاء، وإعادة المحاولة عليه تُراكم ذاكرةً لتُنجي قياساً. أمّا
// فشلٌ متكرّر فيظهر في السجلّات لا في بطء بحث.
// ============================================================================
internal sealed class SearchLogWriterService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SearchLogWriterService> _logger;
    private readonly SearchLogChannel _channel;
    private readonly SearchLogSettings _settings;

    public SearchLogWriterService(
        IServiceProvider services, SearchLogChannel channel, SearchLogSettings settings,
        ILogger<SearchLogWriterService> logger)
    {
        _services = services; _channel = channel; _settings = settings; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ينتظر أول سطر، ثم يفتح نافذةً قصيرة يجمع فيها ما وصل — فلا دورةٌ فارغة كلّ ثانية،
                // ولا سطرٌ ينتظر دورةً كاملة.
                if (!await _channel.Reader.WaitToReadAsync(stoppingToken)) return;
                await Task.Delay(_settings.WriteBatchWindow, stoppingToken);

                var batch = new List<BufferedSearch>();
                while (_channel.Reader.TryRead(out var entry)) batch.Add(entry);
                if (batch.Count == 0) continue;

                await WriteAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // دفعةٌ سقطت: يُسجَّل ويُكمَل. لا إعادة محاولة لقياس.
                _logger.LogWarning(ex, "Search log batch discarded");
            }
        }
    }

    private async Task WriteAsync(List<BufferedSearch> batch, CancellationToken ct)
    {
        // نطاقٌ صريح للدليل: `ITenantDirectory` خدمةٌ بنطاق، وطلبها من المزوّد الجذري يمنع الـ API من
        // الإقلاع في Development (فحص النطاقات مُفعَّل هناك) ويمرّ بصمت في الإنتاج — وهو العيب نفسه
        // الذي وجده M11 في تعبئة فهرس البحث. الاختبارات تفحص النطاقات الآن، فلن يمرّ ثانية.
        using var lookup = _services.CreateScope();
        var directory = lookup.ServiceProvider.GetRequiredService<ITenantDirectory>();

        foreach (var group in batch.GroupBy(b => b.TenantId))
        {
            var tenant = await directory.FindByIdAsync(group.Key, ct);
            // متجرٌ لم يُعثر عليه: لا مسار حذفٍ في النظام (المفتاح الأجنبي `Restrict` يمنعه)، فهذا فرعٌ
            // لا يُتوقَّع بلوغه. ويبقى لأنّ بديله رميةٌ تُسقط دفعةً كاملة لأجل صفٍّ واحد شاذّ.
            if (tenant is null) continue;

            await TenantScopes.RunAsync(_services, tenant, async scoped =>
            {
                var db = scoped.GetRequiredService<AppDbContext>();
                db.SearchQueryLogs.AddRange(group.Select(b => b.Entry));
                await db.SaveChangesAsync(ct);
            });
        }
    }
}
