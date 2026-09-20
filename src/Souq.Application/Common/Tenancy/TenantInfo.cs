using Souq.Domain.Platform;

namespace Souq.Application.Common.Tenancy;

// لقطة المتجر التي يحتاجها طلب واحد: الهوية واللغة والعملة والحالة والوحدات المفعّلة. تُبنى من
// ITenantDirectory (مخزَّنة مؤقتاً لكل مضيف) — لا كيان Tenant متتبَّع يعيش طوال الطلب.
public sealed record TenantInfo(
    int Id, string Slug, string Name, TenantStatus Status,
    string Currency, string DefaultCulture, string TimeZone,
    IReadOnlySet<string> Modules)
{
    // ============================================================================
    // **يفشل مغلقاً** (C1، ADR-0047 §4). كان `Modules is null || Modules.Contains(module)` مع
    // `Modules = null` افتراضياً، أي أن لقطةً بُنيت بلا وحدات تمنح **كل** الوحدات. راحةٌ مقبولة
    // لثلاث ميزات اختيارية، وإهداءٌ للمنتج يوم تصير الوحدة استحقاقاً مدفوعاً.
    //
    // والمعامل بلا قيمة افتراضية عمداً: لو بقيت (ولو إلى مجموعة فارغة) لظلّت مواضع البناء التي
    // تُسقطه تُصرَّف بصمت. إزالتها تجعل المترجم يسمّي كل موضع، وهو الفرق بين إغلاق الثغرة
    // وإغلاق أحد مظاهرها.
    // ============================================================================
    public bool HasModule(string module) => Modules.Contains(module);
}
