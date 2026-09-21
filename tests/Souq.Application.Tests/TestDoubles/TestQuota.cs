using Souq.Application.Features.Billing.Contracts;

namespace Souq.Application.Tests.TestDoubles;

// ============================================================================
// حارس حصص للاختبار (C2). الافتراضي **يسمح بلا قيد** — وهو ما تراه عشرات الاختبارات التي لا شأن
// لها بالحصص: متجرٌ على خطةٍ لا تسمّي هذا الحدّ، وهي الحال الفعلية لكل متجر اليوم.
//
// الاختبارات التي **موضوعها** الحصّة تستعمل `AtLimit` فتردّ رفضاً، كما تبني اختباراتُ الاستحقاق
// مجموعةَ وحداتها صراحةً (TestTenant).
//
// **ولا يُحاكي التزامن**: صواب الحارس تحت السباق لا يُثبَت بضعفٍ في الذاكرة — يُثبَت بقاعدة
// حقيقية في `TenantQuotaConcurrencyTests`، وهذا بالضبط ما يقوله ADR-0049 §الالتزام الرابع.
// ============================================================================
public sealed class TestQuota : ITenantQuotaGuard
{
    private readonly int? _limit;
    private readonly int _used;

    private TestQuota(int? limit, int used)
    {
        _limit = limit; _used = used;
    }

    public static TestQuota Unlimited() => new(null, 0);

    // بلغ سقفه: كل حجزٍ يُرفض.
    public static TestQuota AtLimit(int limit = 1) => new(limit, limit);

    public int Reservations { get; private set; }
    public int Releases { get; private set; }

    public Task<QuotaDecision> ReserveAsync(string limitName, CancellationToken ct = default)
    {
        if (_limit is { } cap && _used >= cap)
            return Task.FromResult(QuotaDecision.Denied(limitName, cap, _used));

        Reservations++;
        return Task.FromResult(_limit is { } granted
            ? QuotaDecision.Granted(limitName, granted, _used + Reservations)
            : QuotaDecision.Uncapped(limitName, _used + Reservations));
    }

    public Task ReleaseAsync(string limitName, int count = 1, CancellationToken ct = default)
    {
        Releases += count;
        return Task.CompletedTask;
    }

    public Task<QuotaDecision> PeekAsync(string limitName, CancellationToken ct = default) =>
        Task.FromResult(_limit is { } cap
            ? QuotaDecision.Granted(limitName, cap, _used)
            : QuotaDecision.Uncapped(limitName, _used));

    public Task<int> ReconcileAsync(CancellationToken ct = default) => Task.FromResult(0);
}
