using Souq.Domain.Common;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// EntitlementOverride — كيف يمنح الدعمُ قدرةً خارج الخطة بلا خلق مصدر حقيقة ثانٍ (ADR-0047 §4).
// جدول منصّة بمفتاح متجر (الشكل B): شرط TenantId صريح في كل قراءة.
//
// ثلاث خصائص تجعله استثناءً لا عَلَماً دائماً — وهي بالضبط ما يفصل الخيار الموصى به في قرار المالك
// C-14 عن "عَلَم ارتجالي" الذي يرفضه السجلّ نفسه:
//   • **ينتهي**: مدّة إلزامية ومحدودة بسقف. استثناءٌ بلا انتهاء هو العَلَم الارتجالي بالضبط.
//   • **منسوب**: حسابُ مَن منحه مكتوب على الصفّ نفسه (كما يفعل AuditEntry.ActorUserId)، فالسؤال
//     "لماذا هذا المتجر يملك هذا؟" له جواب بعد أن يُقصّ سجلّ التدقيق.
//   • **مُدقَّق**: أمره IAuditable كسائر أوامر منطقة المنصّة.
//
// **يمنح ولا يمنع.** المنع له مفتاحه التشغيلي أصلاً (وحدات المتجر)، ولو جاز الاتجاهان لصار
// للسؤال الواحد جوابان — وهو ما تمنعه ADR-0047 من أساسها.
// ============================================================================
public class EntitlementOverride : Entity
{
    public const int ReasonMinLength = 5;
    public const int ReasonMaxLength = 300;
    public const int MaxDurationDays = 90;

    public int TenantId { get; private set; }
    public string Entitlement { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public int GrantedByUserId { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTime? RevokedAtUtc { get; private set; }

    private EntitlementOverride() { }

    public EntitlementOverride(int tenantId, string entitlement, DateTime expiresAtUtc, int grantedByUserId, string reason, DateTime utcNow)
    {
        if (tenantId <= 0) throw new InvalidEntitlementOverrideException("استثناء بلا متجر");
        Entitlement = Entitlements.Normalize(entitlement);

        if (expiresAtUtc <= utcNow)
            throw new InvalidEntitlementOverrideException("انتهاء الاستثناء يجب أن يكون في المستقبل");
        if (expiresAtUtc > utcNow.AddDays(MaxDurationDays))
            throw new InvalidEntitlementOverrideException($"حتى {MaxDurationDays} يوماً للاستثناء — ما طال عن ذلك يُنقل إلى خطة");

        if (grantedByUserId <= 0)
            throw new InvalidEntitlementOverrideException("الاستثناء منسوب لمن منحه");

        var why = reason?.Trim() ?? "";
        if (why.Length < ReasonMinLength || why.Length > ReasonMaxLength)
            throw new InvalidEntitlementOverrideException($"سبب الاستثناء بين {ReasonMinLength} و{ReasonMaxLength} حرفاً");

        TenantId = tenantId;
        ExpiresAtUtc = expiresAtUtc;
        GrantedByUserId = grantedByUserId;
        Reason = why;
    }

    public void Revoke(DateTime utcNow)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = utcNow;
    }

    public bool IsActiveAt(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;
}
