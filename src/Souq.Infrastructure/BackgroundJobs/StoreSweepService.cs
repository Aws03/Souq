using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// أساس المنسّقات الدورية لكل متجر (D-15: خادم .NET خلفي، بلا Hangfire حتى الحاجة). كل دورة يمرّ على المتاجر النشطة
// ويشغّل عمل الدورة داخل نطاق كل متجر — المرشّح وحارس الكتابة يعملان كما في أي طلب HTTP. أي فشل يُسجَّل ولا يوقف
// الخادم ولا بقية المتاجر. يُفترض نسخة واحدة تشغّله؛ نسختان آمنتان (العمليات مضمونة التكرار) لكن بعمل مكرّر — القفل
// الموزّع في مراجعة الجاهزية للإنتاج (المرحلة 23).
// ============================================================================
internal abstract class StoreSweepService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;

    protected StoreSweepService(IServiceProvider services, ILogger logger)
    {
        _services = services; _logger = logger;
    }

    protected abstract string Name { get; }

    // null = المنسّق معطّل بالإعداد (الاختبارات ترسل أوامره مباشرة ولا تسابقها دورة خلفية).
    protected abstract TimeSpan? Interval { get; }

    // عمل دورة لمتجر واحد داخل نطاقه؛ يعيد عدد ما عالجه.
    protected abstract Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Interval is not { } interval)
        {
            _logger.LogInformation("{Sweep} is disabled by configuration", Name);
            return;
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepAsync(stoppingToken);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantInfo> stores;
        try
        {
            await using var scope = _services.CreateAsyncScope();
            stores = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().ListActiveAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Sweep} could not list stores", Name);
            return;
        }

        foreach (var store in stores)
        {
            try
            {
                var processed = await TenantScopes.RunAsync(_services, store, provider => RunForStoreAsync(provider, ct));
                if (processed > 0)
                    _logger.LogInformation("{Sweep} processed {Count} items for store {TenantId}", Name, processed, store.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{Sweep} failed for store {TenantId}", Name, store.Id);
            }
        }
    }
}
