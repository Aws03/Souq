using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Features.Products;
using Souq.Application.Features.Products.Commands;

namespace Souq.Infrastructure.BackgroundJobs;

// منسّق حفظ سجلّ البحث (M13): كل Search:Log:PurgeIntervalMinutes يرسل PurgeSearchLogCommand داخل نطاق كل متجر
// نشط. 0 يعطّله (الاختبارات ترسل الأمر مباشرة). هذه `StoreSweepService` حقّاً — خلاف كاتب السجلّ — لأنّ
// الحفظ واجبٌ على **كل** متجر حتى النائم: متجرٌ تُرك بسجلٍّ قديم يبقى قديماً إلى الأبد لو لم يُمرّ عليه.
internal sealed class SearchLogPurgeService : StoreSweepService
{
    private readonly SearchLogSettings _settings;

    public SearchLogPurgeService(
        IServiceProvider services, SearchLogSettings settings, ILogger<SearchLogPurgeService> logger)
        : base(services, logger) => _settings = settings;

    protected override string Name => "Search log purge";

    protected override TimeSpan? Interval =>
        _settings.PurgeIntervalMinutes > 0 ? TimeSpan.FromMinutes(_settings.PurgeIntervalMinutes) : null;

    protected override Task<int> RunForStoreAsync(IServiceProvider scoped, CancellationToken ct) =>
        scoped.GetRequiredService<IMediator>().Send(new PurgeSearchLogCommand(), ct);
}
