using Souq.Domain.Platform;

namespace Souq.Application.Common.Tenancy;

// لقطة المتجر التي يحتاجها طلب واحد: الهوية واللغة والعملة والحالة. تُبنى من ITenantDirectory
// (مخزَّنة مؤقتاً لكل مضيف) — لا كيان Tenant متتبَّع يعيش طوال الطلب.
public sealed record TenantInfo(
    int Id, string Slug, string Name, TenantStatus Status,
    string Currency, string DefaultCulture, string TimeZone);
