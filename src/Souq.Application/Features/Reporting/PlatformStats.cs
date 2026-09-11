using MediatR;
using Souq.Application.Common.Auditing;

namespace Souq.Application.Features.Reporting;

// ============================================================================
// إحصاءات المنصّة (وحدة Reporting — قراءة فقط، Modules.md): أعداد عبر كل المتاجر لمالك المنصّة ومشرفيها
// (platform.reports.view). القراءة عبر المتاجر في الصنف المُراجَع الوحيد لتجاوز المرشّح، ومُدقَّقة.
// ============================================================================
public sealed record PlatformStatsDto(
    IReadOnlyDictionary<string, int> TenantsByStatus, int PlatformAccounts, int StoreStaffAccounts,
    int Customers, int Products, int Orders, int OrdersLast30Days);

public interface IPlatformReports
{
    Task<PlatformStatsDto> GetStatsAsync(DateTime recentSince, CancellationToken ct);
}

public record GetPlatformStatsQuery : IRequest<PlatformStatsDto>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.stats.viewed");
}

public class GetPlatformStatsHandler : IRequestHandler<GetPlatformStatsQuery, PlatformStatsDto>
{
    private readonly IPlatformReports _reports;
    private readonly TimeProvider _clock;

    public GetPlatformStatsHandler(IPlatformReports reports, TimeProvider clock)
    {
        _reports = reports; _clock = clock;
    }

    public Task<PlatformStatsDto> Handle(GetPlatformStatsQuery query, CancellationToken ct) =>
        _reports.GetStatsAsync(_clock.GetUtcNow().UtcDateTime.AddDays(-30), ct);
}
