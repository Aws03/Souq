using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// الاستحقاقات (ADR-0047 §4) — "هل يجوز لهذا المتجر استعمال X؟": قيمة منطقية، مقابل الحدّ (Limit) الذي
// هو رقم. مفاتيح الاستحقاق هي مفاتيح الوحدات الاختيارية نفسها (StoreModules) عمداً: كتالوج واحد ونقطة
// فرض واحدة (TenantInfo.HasModule). أيّ كتالوج ثانٍ يعني جوابين لسؤال واحد — وهو ما تمنعه ADR-0047.
//
// **الجواب الواحد** يُحسب هنا لا في مكانين: ما يسمح به العقد (خطة المتجر + استثناءات الدعم السارية)
// ∩ ما فعّلته المنصّة فعلاً لهذا المتجر. تقاطعٌ لا اتحاد، ولكلٍّ معناه:
//   • العقد سقفٌ تجاري: لا يُمنح ما لم تمنحه خطة.
//   • مفتاح المنصّة إطفاءٌ تشغيلي: متجر لا يريد التقييمات يُطفئها وإن سمحت خطته.
// وكلاهما يفشل **مغلقاً**: مدخل غائب أو غير قابل للحلّ ⇒ المجموعة الفارغة، لا "كل شيء".
// ============================================================================
public static class Entitlements
{
    // اليوم = وحدات المتجر الاختيارية. قدرة مدفوعة جديدة تُضاف إلى StoreModules فتصبح استحقاقاً
    // بلا كتالوج ثانٍ — ولا تمنحها خطة قائمة، لأن الخطط تسمّي ما تمنحه صراحةً.
    public static IReadOnlyList<string> All => StoreModules.All;

    public static bool IsKnown(string? key) => key is not null && All.Contains(key);

    public static string Normalize(string? key)
    {
        var normalized = key?.Trim().ToLowerInvariant() ?? "";
        if (!IsKnown(normalized))
            throw new InvalidPlanException($"استحقاق غير معروف: {key}");
        return normalized;
    }

    // ما يسمح به العقد: ما تمنحه الخطة، زائد استثناءات الدعم السارية (منحٌ فقط — لا استثناء يمنع،
    // فالمنع له مفتاحه التشغيلي أصلاً ولو جاز الاثنان لصار للسؤال جوابان).
    public static IReadOnlySet<string> Granted(IEnumerable<string>? planGrants, IEnumerable<string>? activeOverrides)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in (planGrants ?? []).Concat(activeOverrides ?? []))
            if (IsKnown(key)) granted.Add(key);
        return granted;
    }

    // الجواب الواحد. null في أيّ طرف ⇒ فارغ: لقطة بُنيت بلا معلومة، أو خطة لم تُحلّ، لا تمنح شيئاً.
    public static IReadOnlySet<string> Effective(IReadOnlySet<string>? granted, IReadOnlySet<string>? enabled)
    {
        // مجموعة جديدة في كل مرّة لا مثيلاً ساكناً مشتركاً: `IReadOnlySet` غلافٌ لا حصانة — مستدعٍ
        // يحوّلها إلى HashSet ويعدّلها كان سيُفسد جواب كل نداء لاحق.
        var effective = new HashSet<string>(StringComparer.Ordinal);
        if (granted is null || enabled is null) return effective;
        foreach (var key in granted)
            if (enabled.Contains(key) && IsKnown(key)) effective.Add(key);
        return effective;
    }
}
