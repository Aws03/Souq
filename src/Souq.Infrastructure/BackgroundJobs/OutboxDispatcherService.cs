using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Infrastructure.Persistence.Outbox;

namespace Souq.Infrastructure.BackgroundJobs;

public sealed class NotificationSettings
{
    // صفر ⇒ المُرسِل الخلفي معطّل (الاختبارات تشغّل دورة المعالجة مباشرة ولا تسابقها دورة خلفية).
    public int DispatchIntervalSeconds { get; set; } = 5;

    // المُنجز يُحذف بعدها؛ الميت يبقى للتشخيص.
    public int RetentionDays { get; set; } = 14;
}

// ============================================================================
// المُرسِل الخلفي لصندوق الصادر (المرحلة 14، D-14/D-15): خادم .NET خلفي بلا Hangfire. كل دورة: معالجة ما حان وقته، وكل ساعة
// حذف المُنجز الأقدم من مدّة الاحتفاظ. أي فشل يُسجَّل ولا يوقف الخادم. نسختان آمنتان (عقد الإيجار في OutboxProcessor).
// ============================================================================
internal sealed class OutboxDispatcherService : BackgroundService
{
    private static readonly TimeSpan PurgeEvery = TimeSpan.FromHours(1);

    private readonly IServiceProvider _services;
    private readonly NotificationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxDispatcherService> _logger;

    public OutboxDispatcherService(
        IServiceProvider services, NotificationSettings settings, TimeProvider clock, ILogger<OutboxDispatcherService> logger)
    {
        _services = services; _settings = settings; _clock = clock; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.DispatchIntervalSeconds == 0)
        {
            _logger.LogInformation("Outbox dispatcher is disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.DispatchIntervalSeconds));
        var lastPurge = DateTime.MinValue;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                var handled = await processor.ProcessDueAsync(stoppingToken);
                if (handled > 0)
                    _logger.LogInformation("Outbox dispatcher handled {Count} messages", handled);

                var now = _clock.GetUtcNow().UtcDateTime;
                if (now - lastPurge >= PurgeEvery)
                {
                    await processor.PurgeProcessedAsync(now.AddDays(-_settings.RetentionDays), stoppingToken);
                    lastPurge = now;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox dispatch cycle failed");
            }
        }
    }
}
