using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// وحدات المتجر الاختيارية (D-11، WhiteLabel.md §2): تفعّلها المنصّة لكل متجر، ويفرضها الخادم (نقطة وحدة
// معطّلة ⇒ 404 ModuleDisabled، وحالة الاستخدام التي تمسّها ترفض) — الواجهة تُخفي فقط. الكتالوج
// والطلبات والعملاء والدفع ليست اختيارية: متجر بلا طلبات ليس متجراً.
// ============================================================================
public static class StoreModules
{
    public const string Promotions = "promotions";   // الكوبونات
    public const string Reviews = "reviews";
    public const string Wishlist = "wishlist";

    public static readonly IReadOnlyList<string> All = [Promotions, Reviews, Wishlist];

    // صيغة التخزين: مفاتيح مرتّبة مفصولة بفواصل؛ الفارغ = لا وحدة اختيارية مفعّلة.
    public static string Format(IEnumerable<string> modules)
    {
        var normalized = modules.Select(m => m?.Trim().ToLowerInvariant() ?? "")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var unknown = normalized.FirstOrDefault(m => !All.Contains(m));
        if (unknown is not null)
            throw new InvalidTenantOperationException($"وحدة غير معروفة: {unknown}");
        return string.Join(',', normalized);
    }

    // قراءة متسامحة: مفتاح أُزيل من المنتج يُتجاهل بدل أن يُسقط المتجر.
    public static IReadOnlySet<string> Parse(string? stored) => new HashSet<string>(
        (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(All.Contains),
        StringComparer.Ordinal);
}
