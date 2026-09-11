using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Inventory.Reservations;
using Souq.Application.Features.Orders.Commands;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.BackgroundJobs;

// ============================================================================
// منسّق انتهاء مهلة الدفع (المرحلة 6، D-15: خادم .NET خلفي، بلا Hangfire حتى الحاجة). كل دورة يمرّ على المتاجر النشطة
// ويرسل ExpireStaleCheckoutsCommand داخل نطاق كل متجر — المرشّح وحارس الكتابة يعملان كما في أي طلب HTTP. أي فشل
// يُسجَّل ولا يوقف الخادم ولا بقية المتاجر. يُفترض نسخة واحدة تشغّله؛ نسختان آمنتان (العمليات مضمونة التكرار
// وrowversion يحسم السباق) لكن بعمل مكرّر — القفل الموزّع في مراجعة الجاهزية للإنتاج (المرحلة 23).
// ============================================================================
internal sealed class ReservationExpiryService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly InventorySettings _settings;
    private readonly ILogger<ReservationExpiryService> _logger;

    public ReservationExpiryService(IServiceProvider services, InventorySettings settings, ILogger<ReservationExpiryService> logger)
    {
        _services = services; _settings = settings; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.SweepIntervalSeconds <= 0)
        {
            _logger.LogInformation("Reservation expiry sweep is disabled (Inventory:SweepIntervalSeconds = 0)");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.SweepIntervalSeconds));
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
            _logger.LogError(ex, "Reservation expiry sweep could not list stores");
            return;
        }

        foreach (var store in stores)
        {
            try
            {
                var settled = await TenantScopes.RunAsync(_services, store,
                    provider => provider.GetRequiredService<IMediator>().Send(new ExpireStaleCheckoutsCommand(), ct));
                if (settled > 0)
                    _logger.LogInformation("Settled {Count} expired checkouts for store {TenantId}", settled, store.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Reservation expiry sweep failed for store {TenantId}", store.Id);
            }
        }
    }
}
