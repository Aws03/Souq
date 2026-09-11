using System.Text.RegularExpressions;

namespace Souq.Domain.Auditing;

// ============================================================================
// AuditEntry — سطر في سجلّ التدقيق (D-17، Security.md §10): من فعل ماذا، في أيّ متجر، ومتى. يُضاف ولا يُعدَّل
// ولا يُحذف أبداً (حارس الكتابة يرفض أي تعديل أو حذف قبل أي SQL). ليس كياناً تجارياً: لا يرث Entity ولا
// مرشّح مستأجر عليه — تكتبه المنصّة عن متجر، ويكتبه المتجر عن نفسه، ويُقرأ عبر استعلام بشرط صريح.
// البيانات الوصفية (Metadata) يختارها الطلب نفسه حقلاً حقلاً — لا نسخ لجسم الطلب (كلمات مرور، رموز).
// ============================================================================
public sealed partial class AuditEntry
{
    public const int ActionMaxLength = 80;
    public const int RoleMaxLength = 30;
    public const int TargetTypeMaxLength = 50;
    public const int TargetIdMaxLength = 100;
    public const int MetadataMaxLength = 4000;
    public const int IpAddressMaxLength = 45;       // IPv6 كاملاً
    public const int CorrelationIdMaxLength = 64;

    public long Id { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string Area { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public int? TenantId { get; private set; }
    public int? ActorUserId { get; private set; }
    public string? ActorRole { get; private set; }
    public string? TargetType { get; private set; }
    public string? TargetId { get; private set; }
    public string? Metadata { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }

    private AuditEntry() { }

    public AuditEntry(
        DateTime occurredAt, string area, string action, int? tenantId, int? actorUserId, string? actorRole,
        string? targetType, string? targetId, string? metadata, string? ipAddress, string? correlationId)
    {
        if (!AuditAreas.All.Contains(area))
            throw new ArgumentException($"منطقة تدقيق غير معروفة: {area}", nameof(area));
        if (action is null || action.Length > ActionMaxLength || !ActionPattern().IsMatch(action))
            throw new ArgumentException($"اسم فعل التدقيق يجب أن يكون مثل tenant.created: {action}", nameof(action));

        OccurredAt = occurredAt;
        Area = area;
        Action = action;
        TenantId = tenantId;
        ActorUserId = actorUserId;
        ActorRole = Clip(actorRole, RoleMaxLength);
        TargetType = Clip(targetType, TargetTypeMaxLength);
        TargetId = Clip(targetId, TargetIdMaxLength);
        Metadata = Clip(metadata, MetadataMaxLength);
        IpAddress = Clip(ipAddress, IpAddressMaxLength);
        CorrelationId = Clip(correlationId, CorrelationIdMaxLength);
    }

    // القصّ لا الرفض: سطر تدقيق ناقص الذيل أفضل من إجراء بلا سطر.
    private static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

    [GeneratedRegex("^[a-z]+(?:[.-][a-z]+)+$")]
    private static partial Regex ActionPattern();
}

// من أيّ منطقة جاء الإجراء: مضيف المنصّة، مضيف متجر، أو عمل داخلي بلا طلب (مهمة خلفية، بذر).
public static class AuditAreas
{
    public const string Platform = "Platform";
    public const string Store = "Store";
    public const string System = "System";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Platform, Store, System };
}
