using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Analytics;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// الكاتبُ الخلفيّ للأحداث السلوكية: ينتظر أوّلَ حدث، يمنح نافذةً قصيرة ليتجمّع ما بعده، يصرف
// القناة، ثم يكتب دفعةً **لكل متجر داخل نطاقه**.
//
// **ليس `StoreSweepService`**: ذاك يمرّ على المتاجر بمؤقّت، وهذا يُوقظه وصولُ حدث ويكتب لمتاجرٍ
// وصلت أحداثُها فقط.
//
// **ولكل متجرٍ حمايتُه** — وهذا هو العطب المُصلَح لا المورَّث: نظيرُه في سجلّ البحث يلفّ الدورة
// كلّها بـ try/catch واحد، فعطبُ دفعةِ متجرٍ واحد (صفٌّ يخالف قيداً، مهلةٌ انتهت) يُسقِط دفعات
// **كل** المتاجر الباقية في تلك الدورة — متاجرٌ لا علاقة لها بالعطب تفقد قياسها بسببه.
// ============================================================================
internal sealed class EventWriterService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly EventChannel _channel;
    private readonly EventCaptureSettings _settings;
    private readonly ILogger<EventWriterService> _logger;

    public EventWriterService(
        IServiceProvider services, EventChannel channel, EventCaptureSettings settings,
        ILogger<EventWriterService> logger)
    {
        _services = services; _channel = channel; _settings = settings; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(stoppingToken)) return;
                await Task.Delay(_settings.WriteBatchWindow, stoppingToken);

                var batch = new List<BufferedEvent>();
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
                // عطبٌ خارج دفعةِ متجرٍ بعينه (الدليل، القناة). الحلقة تستمرّ: كاتبٌ يموت يعني
                // توقّفَ القياس كلّه إلى أن يُعاد النشر.
                _logger.LogError(ex, "The behavioural event writer failed a cycle");
            }
        }
    }

    private async Task WriteAsync(List<BufferedEvent> batch, CancellationToken ct)
    {
        // نطاقٌ صريح للدليل: خدمةٌ ذاتُ نطاق لا تُطلب من مزوّد الجذر (عطبُ إقلاعٍ وقع مرّة).
        await using var scope = _services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<ITenantDirectory>();

        foreach (var group in batch.GroupBy(b => b.TenantId))
        {
            try
            {
                var store = await directory.FindByIdAsync(group.Key, ct);
                if (store is null)
                {
                    _logger.LogWarning("Dropped {EventCount} behavioural events for unknown store {TenantId}",
                        group.Count(), group.Key);
                    continue;
                }

                await TenantScopes.RunAsync(_services, store, async scoped =>
                {
                    var db = scoped.GetRequiredService<AppDbContext>();
                    db.BehaviouralEvents.AddRange(group.Select(b => b.Entry));
                    await db.SaveChangesAsync(ct);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write {EventCount} behavioural events for store {TenantId}",
                    group.Count(), group.Key);
            }
        }
    }
}

// ============================================================================
// مَسْحا الصيانة: تجميعٌ ثم مسح، كلٌّ منهما `StoreSweepService` — فلكل متجرٍ حمايتُه ونطاقُه،
// والفاصلُ صفراً يعني معطّلاً.
//
// **وهما مَسْحان لا واحد، بفاصلين مختلفين عن قصد**: التجميع يُفيد كلّما تكرّر (ساعةً بساعة تبقى
// لوحاتُ التاجر قريبةً من الحقيقة)، والمسح عملٌ ثقيل لا يُراد كثيراً. وجمعُهما في مسحٍ واحد يربط
// وتيرة أحدهما بالآخر بلا سبب.
// ============================================================================
internal sealed class EventRollupService : StoreSweepService
{
    private readonly EventCaptureSettings _settings;

    public EventRollupService(
        IServiceProvider services, EventCaptureSettings settings, ILogger<EventRollupService> logger)
        : base(services, logger) => _settings = settings;

    protected override string Name => "Behavioural event rollup";

    protected override TimeSpan? Interval =>
        _settings.RollupIntervalMinutes > 0 ? TimeSpan.FromMinutes(_settings.RollupIntervalMinutes) : null;

    protected override Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct) =>
        scoped.GetRequiredService<MediatR.IMediator>()
            .Send(new Application.Features.Analytics.Commands.RollUpBehaviouralEventsCommand(), ct);
}

internal sealed class EventPurgeService : StoreSweepService
{
    private readonly EventCaptureSettings _settings;

    public EventPurgeService(
        IServiceProvider services, EventCaptureSettings settings, ILogger<EventPurgeService> logger)
        : base(services, logger) => _settings = settings;

    protected override string Name => "Behavioural event purge";

    protected override TimeSpan? Interval =>
        _settings.PurgeIntervalMinutes > 0 ? TimeSpan.FromMinutes(_settings.PurgeIntervalMinutes) : null;

    protected override Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct) =>
        scoped.GetRequiredService<MediatR.IMediator>()
            .Send(new Application.Features.Analytics.Commands.PurgeBehaviouralEventsCommand(), ct);
}
