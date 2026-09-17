using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Features.Stores;
using Souq.Domain.Identity;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Platform;

// ============================================================================
// ما يقبله تجهيز متجر من المنصّة (GET /api/platform/tenants/options): حدود هوية المتجر (الاسم والمعرّف
// والنطاق)، والوحدات الاختيارية، وخيارات محرّر الإعدادات نفسها التي يقرؤها مدير المتجر — لا نسخة ثانية.
//
// على مضيف المنصّة لأن نقطة خيارات المتجر (/api/admin/store/settings/options) تحتاج متجراً محلولاً من
// المضيف، ومضيف المنصّة لا متجر له. الخيارات قيم المنصّة كلها، بلا بيانات متجر.
// مُدقَّقة كبقية طلبات المنصّة (قاعدة معمارية): قراءة المنصّة صريحة ومُسجَّلة، حتى لو كانت قائمة ثابتة.
// ============================================================================
public sealed record ProvisioningLimitsDto(
    int NameMin, int NameMax, int SlugMin, int SlugMax, int TimeZoneMax, int HostMax, int FullNameMax, int EmailMax);

public sealed record ProvisioningOptionsDto(
    StoreSettingsOptionsDto Settings, IReadOnlyList<string> Modules, IReadOnlyList<string> Statuses,
    IReadOnlyList<string> ReservedSlugs, ProvisioningLimitsDto Limits);

public record GetProvisioningOptionsQuery : IRequest<ProvisioningOptionsDto>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.provisioning.options.viewed");
}

public class GetProvisioningOptionsHandler : IRequestHandler<GetProvisioningOptionsQuery, ProvisioningOptionsDto>
{
    private static readonly ProvisioningOptionsDto Options = new(
        GetStoreSettingsOptionsHandler.Options,
        StoreModules.All,
        Enum.GetNames<TenantStatus>(),
        Tenant.ReservedSlugs.Order(StringComparer.Ordinal).ToList(),
        new ProvisioningLimitsDto(
            Tenant.NameMinLength, Tenant.NameMaxLength, Tenant.SlugMinLength, Tenant.SlugMaxLength,
            Tenant.TimeZoneMaxLength, TenantDomain.HostMaxLength, User.FullNameMaxLength, User.EmailMaxLength));

    public Task<ProvisioningOptionsDto> Handle(GetProvisioningOptionsQuery query, CancellationToken ct) =>
        Task.FromResult(Options);
}
