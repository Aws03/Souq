using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Features.Baskets;

namespace Souq.Infrastructure.BackgroundJobs;

// منسّق حذف السلال المنتهية (المرحلة 8، ADR-0028): كل Basket:CleanupIntervalMinutes يرسل PurgeExpiredBasketsCommand داخل
// نطاق كل متجر نشط. 0 يعطّله.
internal sealed class BasketCleanupService : StoreSweepService
{
    private readonly BasketSettings _settings;

    public BasketCleanupService(IServiceProvider services, BasketSettings settings, ILogger<BasketCleanupService> logger)
        : base(services, logger) => _settings = settings;

    protected override string Name => "Basket cleanup";

    protected override TimeSpan? Interval =>
        _settings.CleanupIntervalMinutes > 0 ? TimeSpan.FromMinutes(_settings.CleanupIntervalMinutes) : null;

    protected override Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct) =>
        scoped.GetRequiredService<IMediator>().Send(new PurgeExpiredBasketsCommand(), ct);
}
