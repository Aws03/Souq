using Souq.Application.Common.Accounts;
using Souq.Application.Common.Models;
using Souq.Application.Features.Stores;
using Souq.Domain.Common;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Platform;

// ============================================================================
// منطقة المنصّة (المرحلة 4): مدخل مالك المنصّة ومشرفيها إلى وحدات Platform وIdentity وReporting — لا وحدة
// بياناتها الخاصة (Modules.md §1). هنا وحدها تحمل الطلبات معرّف متجر (اختبار معماري)، وكلها مُدقَّقة.
// ============================================================================

// ActiveAdmins / PendingAdminInvitations: جاهزية التسليم لا نشاط تجاري — "هل يستطيع أحدٌ إدارة هذا المتجر؟".
// مدير فعّال = حساب TenantAdmin غير موقوف قَبِل دعوته. لا أرقام مبيعات متجرٍ بعينه في قائمة المنصّة.
public sealed record TenantSummaryDto(
    int Id, string Name, string Slug, string Status, string Currency, string DefaultCulture,
    string? PrimaryHost, int DomainCount, DateTime CreatedAt, int ActiveAdmins, int PendingAdminInvitations);

public sealed record TenantDomainDto(string Host, bool IsPrimary, DateTime? VerifiedAt);

// Modules = مفتاح المنصّة التشغيلي (ما أُبقي مفعّلاً لهذا المتجر). EffectiveModules = الجواب الواحد
// الذي يفرضه الخادم فعلاً (C1): مفتاح المنصّة ∩ ما يسمح به عقد المتجر. عرضُ الأول وحده يجعل المشغّل
// يرى مربّعاً مؤشَّراً لوحدةٍ لا تعمل، بلا ما يفسّر السبب — فالحقلان معاً أو لا شيء.
public sealed record TenantDetailDto(
    int Id, string Name, string Slug, string Status, string Currency, string DefaultCulture, string TimeZone,
    DateTime CreatedAt, IReadOnlyList<TenantDomainDto> Domains, IReadOnlyList<string> Modules, StoreSettingsDto Settings,
    IReadOnlyList<string> EffectiveModules, string? PlanCode, string? PlanName);

public sealed record AuditEntryDto(
    long Id, DateTime OccurredAt, string Area, string Action, int? TenantId, int? ActorUserId, string? ActorRole,
    string? TargetType, string? TargetId, string? Metadata, string? IpAddress, string? CorrelationId);

public sealed record TenantListFilter(string? Search, TenantStatus? Status);

public sealed record AuditFilter(int? TenantId, string? Action, int? ActorUserId, DateTime? From, DateTime? To);

// ============================================================================
// منفذ القراءة لمنطقة المنصّة (ADR-0008). ما يعبر بيانات المتاجر (حسابات متجر، نشاطه التجاري) يتجاوز مرشّح
// المستأجر بشرط TenantId صريح — في الصنف المُراجَع الوحيد (PlatformQueries، اختبار معماري).
// ============================================================================
public interface IPlatformQueries
{
    Task<PaginatedList<TenantSummaryDto>> ListTenantsAsync(TenantListFilter filter, PageRequest page, CancellationToken ct);
    Task<TenantDetailDto?> GetTenantAsync(int tenantId, CancellationToken ct);
    Task<PaginatedList<AccountSummaryDto>> ListTenantAccountsAsync(int tenantId, PageRequest page, CancellationToken ct);
    Task<bool> HasCommercialActivityAsync(int tenantId, CancellationToken ct);
    Task<PaginatedList<AuditEntryDto>> ListAuditAsync(AuditFilter filter, PageRequest page, CancellationToken ct);
}

public static class PlatformRoles
{
    public static readonly IReadOnlyCollection<string> All = [Roles.PlatformOwner, Roles.PlatformAdmin];

    // اسم المُرسِل في رسائل دعوة حسابات المنصّة.
    public const string InviterName = "لوحة منصّة سوق";
}
