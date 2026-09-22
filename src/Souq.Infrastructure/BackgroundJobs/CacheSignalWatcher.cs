using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;
using Souq.Domain.Platform;
using Souq.Infrastructure.Services;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// مراقبُ إشارات الإبطال (C4، [ADR-0057](0057)): يسأل الجدولَ كلَّ بضع ثوانٍ «هل أبطل أحدٌ شيئاً؟»،
// وحين يكبر جيلُ إشارةٍ يُبطل الذاكرةَ المقابلة **في هذه النسخة**.
//
// ============================================================================
// **ولا قفلَ عليه، ولا يُراد له قفل.** هذا العمل الخلفيّ الوحيد الذي **يجب** أن تجريه كلُّ نسخة:
// ما يفعله محلّيٌّ بحت — تحديثُ ذاكرةِ العملية التي يعيش فيها — ووضعُه تحت عقدِ إيجار كان سيعني
// أنّ نسخةً واحدة تُبطل ذاكرتَها والباقيات يبقين على القديم إلى الأبد. وهو عكسُ الغرض تماماً.
// ============================================================================
//
// **وأوّلُ دورةٍ تقرأ خطَّ الأساس ولا تُبطل شيئاً.** النسخةُ تُقلع بذاكرةٍ فارغة أصلاً، فإبطالُها
// عملٌ بلا أثر — لكنّه كان سيعني أنّ كلَّ نشرٍ يبدأ بموجةِ استعلاماتٍ لا سبب لها.
//
// **والفشلُ لا يوقف المراقب.** انقطاعُ القاعدة لحظةً يعني دورةً فائتة لا خدمةً ساقطة: الذاكراتُ
// لها مدّةُ انتهاءٍ أصلاً، فأسوأُ ما يقع أن تعود النسخةُ إلى سلوكِ ما قبل هذه الشريحة حتى تعود
// القاعدة.
// ============================================================================
internal sealed class CacheSignalWatcher : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly TenantDirectoryCache _directory;
    private readonly SessionStampCache _sessions;
    private readonly CoordinationSettings _settings;
    private readonly ILogger<CacheSignalWatcher> _logger;

    // آخرُ جيلٍ رأته هذه النسخة لكل إشارة. `null` قبل أوّل قراءة = لم يُؤخَذ خطُّ الأساس بعد.
    private Dictionary<string, long>? _seen;

    public CacheSignalWatcher(
        IServiceProvider services, TenantDirectoryCache directory, SessionStampCache sessions,
        CoordinationSettings settings, ILogger<CacheSignalWatcher> logger)
    {
        _services = services; _directory = directory; _sessions = sessions;
        _settings = settings; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.CacheSignalPollSeconds <= 0)
        {
            // صفر يعطّله — كما تفعل بقيّة المنسّقات، وللسبب نفسه: اختبارٌ يفحص ذاكرةً لا تسابقه
            // دورةٌ خلفية تُبطلها تحته.
            _logger.LogInformation("Cache signal watcher is disabled by configuration");
            return;
        }

        var interval = TimeSpan.FromSeconds(_settings.CacheSignalPollSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollAsync(stoppingToken);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        IReadOnlyDictionary<string, long> current;
        try
        {
            await using var scope = _services.CreateAsyncScope();
            current = await scope.ServiceProvider.GetRequiredService<ICacheSignals>().ReadAllAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache signal poll failed; this instance keeps its caches until their TTL");
            return;
        }

        if (_seen is null)
        {
            // خطُّ الأساس: ما كان موجوداً لحظةَ الإقلاع ليس تغييراً يخصّنا.
            _seen = new Dictionary<string, long>(current, StringComparer.Ordinal);
            return;
        }

        foreach (var (name, version) in current)
        {
            if (_seen.TryGetValue(name, out var last) && last >= version) continue;
            _seen[name] = version;

            switch (name)
            {
                case CacheSignal.TenantDirectory:
                    _directory.Invalidate();
                    break;
                case CacheSignal.SessionStamps:
                    _sessions.Clear();
                    break;
                default:
                    // إشارةٌ لا تعرفها هذه النسخة: نشرٌ أحدثُ يكتب اسماً لم يوجد بعد. تُتجاهَل
                    // ولا تُرمى — ونسختان بنسختَي شيفرة مختلفتين حالةٌ عاديّة أثناء النشر.
                    _logger.LogDebug("Unknown cache signal {Signal}; ignored", name);
                    break;
            }
        }
    }
}

// ============================================================================
// إعدادُ التنسيق بين النسخ. القسم `Coordination`.
// ============================================================================
public sealed class CoordinationSettings
{
    // ============================================================================
    // خمسُ ثوانٍ افتراضاً. **وهي مقايضةٌ صريحة لا رقمٌ اعتباطيّ**: أقصرُ منها يعني استعلاماً على
    // جدولٍ من صفّين أكثرَ تواتراً بلا أن يشتري شيئاً يُذكر، وأطولُ يُوسّع نافذةَ التقادم التي
    // وُجدت هذه الآلية لتضييقها.
    //
    // وما تعنيه عملياً: تعليقُ متجرٍ يصل إلى بقيّة النسخ خلال خمس ثوانٍ بدل ستّين، وإبطالُ جلسةٍ
    // خلال خمسٍ بدل ثلاثين. والفجوةُ تبقى — انظر `CacheSignal` — لكنّها صارت من رتبةٍ أخرى.
    // ============================================================================
    public int CacheSignalPollSeconds { get; set; } = 5;
}
