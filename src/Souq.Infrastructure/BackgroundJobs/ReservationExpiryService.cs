using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Features.Inventory.Reservations;
using Souq.Application.Features.Orders.Commands;

namespace Souq.Infrastructure.BackgroundJobs;

// منسّق انتهاء مهلة الدفع (المرحلة 6، ADR-0026): كل Inventory:SweepIntervalSeconds يرسل ExpireStaleCheckoutsCommand داخل
// نطاق كل متجر نشط. 0 يعطّله.
internal sealed class ReservationExpiryService : StoreSweepService
{
    private readonly InventorySettings _settings;

    public ReservationExpiryService(IServiceProvider services, InventorySettings settings, ILogger<ReservationExpiryService> logger)
        : base(services, logger) => _settings = settings;

    protected override string Name => "Reservation expiry sweep";

    protected override TimeSpan? Interval =>
        _settings.SweepIntervalSeconds > 0 ? TimeSpan.FromSeconds(_settings.SweepIntervalSeconds) : null;

    protected override Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct) =>
        scoped.GetRequiredService<IMediator>().Send(new ExpireStaleCheckoutsCommand(), ct);
}
