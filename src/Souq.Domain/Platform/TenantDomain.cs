using System.Text.RegularExpressions;
using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// TenantDomain — مضيف (نطاق) يُخدَم عليه متجر: client-a.com أو client-a.souq.app. جزء من تجمّع
// Tenant (لا يُنشأ إلا عبر Tenant.AddDomain). المضيف هو مفتاح تحديد المستأجر لكل طلب (ADR-0006)،
// لذا فريد على مستوى المنصّة كلها (قيد فريد في القاعدة). ليس ITenantOwned عمداً: خريطة
// المضيفين تُقرأ قبل معرفة المستأجر — هي ما يُعرِّفه.
// ============================================================================
public partial class TenantDomain : Entity
{
    public const int HostMaxLength = 253;

    public int TenantId { get; private set; }
    public string Host { get; private set; } = default!;
    public bool IsPrimary { get; private set; }

    // تاريخ تأكيد ملكية النطاق (DNS/يدوياً من المنصّة) — null = لم يُتحقَّق بعد.
    public DateTime? VerifiedAt { get; private set; }

    private TenantDomain() { }

    internal TenantDomain(string host, bool isPrimary)
    {
        Host = NormalizeHost(host);
        IsPrimary = isPrimary;
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    public void MarkVerified(DateTime utcNow) => VerifiedAt ??= utcNow;

    // صيغة واحدة لكل مضيف في النظام: أحرف صغيرة، بلا منفذ ولا نقطة أخيرة، تسميات DNS صالحة.
    // "Client-A.COM." و"client-a.com" مضيف واحد — وإلا لسجّل متجران النطاق نفسه بحالتين.
    public static string NormalizeHost(string host) =>
        TryNormalizeHost(host) ?? throw new InvalidTenantOperationException($"اسم نطاق غير صالح: {host}");

    public static string? TryNormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized.Length <= HostMaxLength && HostPattern().IsMatch(normalized) ? normalized : null;
    }

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex HostPattern();
}
