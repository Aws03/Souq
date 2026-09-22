using Souq.Application.Common.Models;

namespace Souq.Application.Features.Billing;

// ============================================================================
// وحدة Billing (C1، ADR-0047) — مستوى التحكّم التجاري: **مَن يستحقّ ماذا**، لا كم يدفع.
// الفوترة والفواتير والعمولات والتحصيل كلّها لاحقة (C5 وما بعدها) وبعضها موقوف على قرار مالك.
//
// الوحدة تملك الخطة والاشتراك والاستثناء. **الفرض يبقى حيث هو**: TenantInfo.HasModule ونقطة
// TenantAvailability — لا فحص ثانٍ ولا نظام أعلام ثانٍ. ما تفعله هذه الوحدة هو أنها **تُغذّي**
// اللقطة التي يقرؤها ذلك الفحص، ودليلُ المتاجر هو من يدمج المدخلات.
//
// وطريق مال المتسوّق (وحدة Payments) لا يلتقي بهذا الطريق إطلاقاً: مسارٌ بنطاق متجر وسجلّاته
// مرتبطة بطلب، وهذا بنطاق المنصّة وبلا طلب أصلاً (CommercialPlatformArchitecture §5.1).
// ============================================================================

public sealed record PlanLimitDto(string Name, int Value);

// السعرُ يُقرأ حيث تُقرأ الخطة (C5، ADR-0056). `null` ⇒ **بلا سعر**: لم يُقرَّر بعد، ولا تُصدَر
// عنها فاتورةُ اشتراك — وهو غيرُ «سعرُها صفر»، وذلك الفرقُ مقصود.
public sealed record PlanSummaryDto(
    int Id, string Code, int Version, string Name, string Status, int SubscriberCount,
    decimal? PriceAmount, string? PriceCurrency, int BillingIntervalMonths);

public sealed record PlanDetailDto(
    int Id, string Code, int Version, string Name, string Status,
    IReadOnlyList<string> Entitlements, IReadOnlyList<PlanLimitDto> Limits,
    int SubscriberCount, DateTime CreatedAt,
    decimal? PriceAmount, string? PriceCurrency, int BillingIntervalMonths);

public sealed record EntitlementOverrideDto(
    int Id, string Entitlement, DateTime ExpiresAtUtc, string Reason,
    int GrantedByUserId, string? GrantedByEmail, DateTime? RevokedAtUtc, bool IsActive);

// ============================================================================
// الجواب الواحد، مشروحاً: ما يراه المشغّل حين يسأل "لماذا هذا المتجر يملك هذه الوحدة؟".
// المُخرَج (Effective) هو بعينه ما تحمله TenantInfo.Modules، والمداخل الثلاثة معروضة بجانبه كي
// لا يُخمَّن السبب: استحقاقات الخطة، استثناءات الدعم السارية، ومفتاح المنصّة لكل وحدة.
// ============================================================================
public sealed record TenantEntitlementsDto(
    int TenantId,
    int? PlanId, string? PlanCode, int? PlanVersion, string? PlanName,
    string? SubscriptionStatus, DateTime? SubscribedAtUtc,
    IReadOnlyList<string> PlanEntitlements,
    IReadOnlyList<string> OverrideEntitlements,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<string> Effective,
    IReadOnlyList<PlanLimitDto> Limits);

public sealed record PlanListFilter(string? Search, bool IncludeRetired);

// ============================================================================
// منفذ القراءة لوحدة Billing (ADR-0008). تنفيذه (BillingQueries) داخلي في Infrastructure —
// وهو من الأنواع المراجَعة المسموح لها بقراءة جداول المنصّة ذات مفتاح المتجر: كل قراءة تخصّ
// متجراً تحمل شرط TenantId صريحاً، لأن لا مرشّح على تلك الجداول.
// ============================================================================
public interface IBillingQueries
{
    Task<PaginatedList<PlanSummaryDto>> ListPlansAsync(PlanListFilter filter, PageRequest page, CancellationToken ct);
    Task<PlanDetailDto?> GetPlanAsync(int planId, CancellationToken ct);
    Task<TenantEntitlementsDto?> GetTenantEntitlementsAsync(int tenantId, CancellationToken ct);
    Task<IReadOnlyList<EntitlementOverrideDto>> ListOverridesAsync(int tenantId, CancellationToken ct);
}
