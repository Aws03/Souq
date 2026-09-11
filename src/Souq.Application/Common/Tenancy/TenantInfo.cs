using Souq.Domain.Platform;

namespace Souq.Application.Common.Tenancy;

// لقطة المتجر التي يحتاجها طلب واحد: الهوية واللغة والعملة والحالة والوحدات المفعّلة. تُبنى من
// ITenantDirectory (مخزَّنة مؤقتاً لكل مضيف) — لا كيان Tenant متتبَّع يعيش طوال الطلب.
public sealed record TenantInfo(
    int Id, string Slug, string Name, TenantStatus Status,
    string Currency, string DefaultCulture, string TimeZone,
    IReadOnlySet<string>? Modules = null)
{
    // null (لقطة بُنيت بلا وحدات — اختبارات، بذر) ⇒ كل الوحدات مفعّلة.
    public bool HasModule(string module) => Modules is null || Modules.Contains(module);
}
