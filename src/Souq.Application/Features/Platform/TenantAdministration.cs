using FluentValidation;
using MediatR;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing.Contracts;
using Souq.Application.Features.Stores;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Platform;

// ============================================================================
// إدارة المتاجر من المنصّة (صلاحية platform.tenants.manage): الإنشاء، الملف، دورة الحياة، النطاقات، الإعدادات،
// الوحدات، ملفات الهوية، ودعوة المدير. القواعد في تجمّع Tenant؛ هنا التنسيق فقط. كل تغيير يُبطل دليل المتاجر
// (المضيفون والحالة والوحدات) فيسري على هذه النسخة فوراً وعلى غيرها خلال دقيقة. الكتابة داخل متجر (مديره،
// ملفاته) تمرّ عبر ITenantScopeRunner — نطاق ذلك المتجر وحرّاسه — لا بتجاوز المرشّح.
// ============================================================================

internal static class PlatformTenants
{
    public static Error NotFound => Error.NotFound("المتجر غير موجود");

    public static Dictionary<string, object?> Meta(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}

// ── القراءة ─────────────────────────────────────────────────────────────────

public record ListTenantsQuery(string? Search = null, TenantStatus? Status = null, int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<TenantSummaryDto>>, IPagedQuery, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.tenants.listed");
}

public sealed class ListTenantsQueryValidator : PagedQueryValidator<ListTenantsQuery>
{
    public ListTenantsQueryValidator() => RuleFor(x => x.Search).MaximumLength(100);
}

public class ListTenantsHandler : IRequestHandler<ListTenantsQuery, PaginatedList<TenantSummaryDto>>
{
    private readonly IPlatformQueries _queries;
    public ListTenantsHandler(IPlatformQueries queries) => _queries = queries;

    public Task<PaginatedList<TenantSummaryDto>> Handle(ListTenantsQuery q, CancellationToken ct) =>
        _queries.ListTenantsAsync(new TenantListFilter(q.Search?.Trim(), q.Status), PageRequest.From(q), ct);
}

public record GetTenantQuery(int TenantId) : IRequest<Result<TenantDetailDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.tenant.viewed", "Tenant", TenantId.ToString(), TenantId);
}

public class GetTenantHandler : IRequestHandler<GetTenantQuery, Result<TenantDetailDto>>
{
    private readonly IPlatformQueries _queries;
    private readonly ITenantDirectory _directory;
    private readonly IStoreEntitlements _entitlements;

    public GetTenantHandler(IPlatformQueries queries, ITenantDirectory directory, IStoreEntitlements entitlements)
    {
        _queries = queries; _directory = directory; _entitlements = entitlements;
    }

    public async Task<Result<TenantDetailDto>> Handle(GetTenantQuery q, CancellationToken ct)
    {
        if (await _queries.GetTenantAsync(q.TenantId, ct) is not { } tenant)
            return Result<TenantDetailDto>.Failure(PlatformTenants.NotFound);

        // الوحدات الفعّالة من **الدليل** لا من حساب ثانٍ هنا: قاعدة الدمج تعيش في مكان واحد
        // (TenantDirectory)، ونسخُها إلى شاشة المنصّة كان سيصنع جواباً ثانياً يتباعد عن الأول بصمت.
        var snapshot = await _directory.FindByIdAsync(q.TenantId, ct);
        var plan = await _entitlements.GetPlanSummaryAsync(q.TenantId, ct);
        return Result<TenantDetailDto>.Success(tenant with
        {
            EffectiveModules = snapshot is null ? [] : snapshot.Modules.Order(StringComparer.Ordinal).ToList(),
            PlanCode = plan?.Code,
            PlanName = plan?.Name,
        });
    }
}

// حسابات إدارة المتجر (مديرون وموظّفون) كما تراها المنصّة للدعم — لا حسابات عملائه.
public record ListTenantAccountsQuery(int TenantId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PaginatedList<AccountSummaryDto>>>, IPagedQuery, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.tenant.accounts.viewed", "Tenant", TenantId.ToString(), TenantId);
}

public sealed class ListTenantAccountsQueryValidator : PagedQueryValidator<ListTenantAccountsQuery>;

public class ListTenantAccountsHandler : IRequestHandler<ListTenantAccountsQuery, Result<PaginatedList<AccountSummaryDto>>>
{
    private readonly IPlatformQueries _queries;
    private readonly ITenantDirectory _directory;

    public ListTenantAccountsHandler(IPlatformQueries queries, ITenantDirectory directory)
    {
        _queries = queries; _directory = directory;
    }

    public async Task<Result<PaginatedList<AccountSummaryDto>>> Handle(ListTenantAccountsQuery q, CancellationToken ct)
    {
        if (await _directory.FindByIdAsync(q.TenantId, ct) is null)
            return Result<PaginatedList<AccountSummaryDto>>.Failure(PlatformTenants.NotFound);
        return Result<PaginatedList<AccountSummaryDto>>.Success(
            await _queries.ListTenantAccountsAsync(q.TenantId, PageRequest.From(q), ct));
    }
}

// ── الإنشاء والملف ودورة الحياة ─────────────────────────────────────────────

// متجر جديد يبدأ قيد التجهيز (Provisioning): إعدادات افتراضية وكل الوحدات، بلا نطاق ولا مدير بعد.
public record CreateTenantCommand(string Name, string Slug, string Currency, string DefaultCulture, string TimeZone)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.created", "Tenant", Slug?.Trim().ToLowerInvariant(),
        Metadata: PlatformTenants.Meta(("name", Name), ("currency", Currency), ("defaultCulture", DefaultCulture)));
}

public sealed class CreateTenantValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(Tenant.SlugMaxLength);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.DefaultCulture).NotEmpty();
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(Tenant.TimeZoneMaxLength);
    }
}

public class CreateTenantHandler : IRequestHandler<CreateTenantCommand, Result<int>>
{
    private readonly ITenantRepository _tenants;
    private readonly IStoreEntitlements _entitlements;
    private readonly ITenantDirectory _directory;
    private readonly IUnitOfWork _uow;

    public CreateTenantHandler(
        ITenantRepository tenants, IStoreEntitlements entitlements, ITenantDirectory directory, IUnitOfWork uow)
    {
        _tenants = tenants; _entitlements = entitlements; _directory = directory; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateTenantCommand cmd, CancellationToken ct)
    {
        var tenant = new Tenant(cmd.Name, cmd.Slug, cmd.Currency, cmd.DefaultCulture, cmd.TimeZone);
        if (await _tenants.SlugExistsAsync(tenant.Slug, ct))
            return Result<int>.Failure(Error.Conflict("TenantSlugTaken", "هذا المعرّف مستخدم لمتجر آخر"));

        await _tenants.AddAsync(tenant, ct);
        await _uow.SaveChangesAsync(ct);

        // الخطة التأسيسية (C1): متجر بلا اشتراك لا يملك وحدة اختيارية واحدة — الاستحقاق يفشل مغلقاً.
        // فإسنادها هنا هو ما يُبقي التجهيز كما كان قبل وجود الخطط. عبر عقد Billing لا بأنواعها: وإلّا
        // صارت Platform ⇄ Billing دورةً في اتجاهَي المجال معاً.
        await _entitlements.AssignFoundationPlanAsync(tenant.Id, ct);

        _directory.Invalidate();
        return Result<int>.Success(tenant.Id);
    }
}

// الاسم والعملة: قرارات المنصّة (العملة تُقفل بعد أول منتج أو طلب — تاريخ مالي لا يُكسر).
public record UpdateTenantCommand(int TenantId, string Name, string Currency) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.updated", "Tenant", TenantId.ToString(), TenantId,
        PlatformTenants.Meta(("name", Name), ("currency", Currency)));
}

public sealed class UpdateTenantValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public class UpdateTenantHandler : IRequestHandler<UpdateTenantCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly IPlatformQueries _queries;
    private readonly ITenantDirectory _directory;
    private readonly IUnitOfWork _uow;

    public UpdateTenantHandler(ITenantRepository tenants, IPlatformQueries queries, ITenantDirectory directory, IUnitOfWork uow)
    {
        _tenants = tenants; _queries = queries; _directory = directory; _uow = uow;
    }

    public async Task<Result> Handle(UpdateTenantCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null) return Result.Failure(PlatformTenants.NotFound);

        tenant.Rename(cmd.Name);
        if (!string.Equals(tenant.Currency, cmd.Currency?.Trim(), StringComparison.OrdinalIgnoreCase))
            tenant.ChangeCurrency(cmd.Currency!, await _queries.HasCommercialActivityAsync(tenant.Id, ct));

        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result.Success();
    }
}

public enum TenantLifecycleAction { Activate, Suspend, Archive }

// التفعيل يفتح الواجهة؛ الإيقاف يغلقها (503 StoreUnavailable) مع بقاء البيانات؛ الأرشفة نهائية.
public record ChangeTenantStatusCommand(int TenantId, TenantLifecycleAction Action) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new(Action switch
    {
        TenantLifecycleAction.Activate => "tenant.activated",
        TenantLifecycleAction.Suspend => "tenant.suspended",
        _ => "tenant.archived",
    }, "Tenant", TenantId.ToString(), TenantId);
}

public sealed class ChangeTenantStatusValidator : AbstractValidator<ChangeTenantStatusCommand>
{
    public ChangeTenantStatusValidator() => RuleFor(x => x.Action).IsInEnum();
}

// ============================================================================
// الأرشفة تُبطل جلسات المتجر كلها؛ الإيقاف لا (TD-66، وقرار المالك C-17 = B).
//
// الفرق ليس تفضيلاً: الإيقاف مؤقّت ومعناه «إدارة فقط» — التاجر يدخل ليُصلح سببه، فإبطال جلسته
// يمنعه مما وُجد القرار ليُبقيه. والأرشفة نهائية، وكانت هي الثقب: نقاط الإدارة تردّ 503 للمؤرشف،
// لكن `/api/auth/refresh` مفتوحةٌ له عمداً (كي يرى صاحب متجرٍ مغلقٍ حالته)، فكانت جلسةُ إدارته
// قابلةً للتجديد بلا نهاية بعد إغلاقٍ لا رجعة فيه.
//
// والإبطال داخل معاملة الحفظ نفسها: متجرٌ صار مؤرشفاً وجلساته حيّة حالةٌ لا يجب أن توجد لحظةً.
// ============================================================================
public class ChangeTenantStatusHandler : IRequestHandler<ChangeTenantStatusCommand, Result>
{
    private const string ArchivedReason = "store.archived";

    private readonly ITenantRepository _tenants;
    private readonly ITenantDirectory _directory;
    private readonly IStoreSessionRevoker _sessions;
    private readonly IUnitOfWork _uow;

    public ChangeTenantStatusHandler(
        ITenantRepository tenants, ITenantDirectory directory, IStoreSessionRevoker sessions, IUnitOfWork uow)
    {
        _tenants = tenants; _directory = directory; _sessions = sessions; _uow = uow;
    }

    public async Task<Result> Handle(ChangeTenantStatusCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null) return Result.Failure(PlatformTenants.NotFound);

        switch (cmd.Action)
        {
            case TenantLifecycleAction.Activate: tenant.Activate(); break;
            case TenantLifecycleAction.Suspend: tenant.Suspend(); break;
            default: tenant.Archive(); break;
        }

        if (cmd.Action == TenantLifecycleAction.Archive)
            await _uow.InTransactionAsync(async () =>
            {
                await _uow.SaveChangesAsync(ct);
                await _sessions.RevokeAllAsync(tenant.Id, ArchivedReason, ct);
            }, ct);
        else
            await _uow.SaveChangesAsync(ct);

        _directory.Invalidate();
        return Result.Success();
    }
}

// ── النطاقات ─────────────────────────────────────────────────────────────────

public enum TenantDomainAction { Add, Remove, SetPrimary, Verify }

// أمر واحد لعمليات النطاق الأربع: القواعد نفسها (متجر موجود، مضيف صالح)، والتدقيق يسمّي العملية.
public record ChangeTenantDomainCommand(int TenantId, string Host, TenantDomainAction Action) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new(Action switch
    {
        TenantDomainAction.Add => "tenant.domain.added",
        TenantDomainAction.Remove => "tenant.domain.removed",
        TenantDomainAction.SetPrimary => "tenant.domain.primary-set",
        _ => "tenant.domain.verified",
    }, "Tenant", TenantId.ToString(), TenantId, PlatformTenants.Meta(("host", Host)));
}

public sealed class ChangeTenantDomainValidator : AbstractValidator<ChangeTenantDomainCommand>
{
    public ChangeTenantDomainValidator()
    {
        RuleFor(x => x.Host).NotEmpty().MaximumLength(TenantDomain.HostMaxLength);
        RuleFor(x => x.Action).IsInEnum();
    }
}

public class ChangeTenantDomainHandler : IRequestHandler<ChangeTenantDomainCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantDirectory _directory;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly IPlatformHosts _platformHosts;

    public ChangeTenantDomainHandler(
        ITenantRepository tenants, ITenantDirectory directory, IUnitOfWork uow, TimeProvider clock, IPlatformHosts platformHosts)
    {
        _tenants = tenants; _directory = directory; _uow = uow; _clock = clock; _platformHosts = platformHosts;
    }

    public async Task<Result> Handle(ChangeTenantDomainCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null) return Result.Failure(PlatformTenants.NotFound);

        var host = TenantDomain.NormalizeHost(cmd.Host);
        switch (cmd.Action)
        {
            case TenantDomainAction.Add:
                if (_platformHosts.IsPlatformHost(host))
                    return Result.Failure(Error.Conflict("DomainReserved", "هذا المضيف مضيف المنصّة نفسها ولا يُربط بمتجر"));
                if (await _tenants.HostTakenAsync(host, ct))
                    return Result.Failure(Error.Conflict("DomainTaken", "هذا النطاق مربوط بمتجر على المنصّة"));
                tenant.AddDomain(host);
                break;
            case TenantDomainAction.Remove: tenant.RemoveDomain(host); break;
            case TenantDomainAction.SetPrimary: tenant.SetPrimaryDomain(host); break;
            default: tenant.VerifyDomain(host, _clock.GetUtcNow().UtcDateTime); break;
        }
        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result.Success();
    }
}

// ── الإعدادات والوحدات وملفات الهوية ────────────────────────────────────────

public record UpdateTenantSettingsCommand(int TenantId, StoreSettingsInput Settings) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.settings.updated", "Tenant", TenantId.ToString(), TenantId);
}

public sealed class UpdateTenantSettingsValidator : AbstractValidator<UpdateTenantSettingsCommand>
{
    public UpdateTenantSettingsValidator() =>
        RuleFor(x => x.Settings).NotNull().SetValidator(new StoreSettingsInputValidator());
}

public class UpdateTenantSettingsHandler : IRequestHandler<UpdateTenantSettingsCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantDirectory _directory;
    private readonly IUnitOfWork _uow;

    public UpdateTenantSettingsHandler(ITenantRepository tenants, ITenantDirectory directory, IUnitOfWork uow)
    {
        _tenants = tenants; _directory = directory; _uow = uow;
    }

    public async Task<Result> Handle(UpdateTenantSettingsCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null) return Result.Failure(PlatformTenants.NotFound);

        StoreSettingsEditor.Apply(tenant, cmd.Settings);
        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result.Success();
    }
}

// الوحدات الاختيارية المفعّلة (D-11): القائمة كاملة تستبدل الحالية؛ مفتاح مجهول ⇒ 422.
public record SetTenantModulesCommand(int TenantId, IReadOnlyList<string> Modules) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.modules.updated", "Tenant", TenantId.ToString(), TenantId,
        PlatformTenants.Meta(("modules", Modules)));
}

public sealed class SetTenantModulesValidator : AbstractValidator<SetTenantModulesCommand>
{
    public SetTenantModulesValidator() => RuleFor(x => x.Modules).NotNull();
}

public class SetTenantModulesHandler : IRequestHandler<SetTenantModulesCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantDirectory _directory;
    private readonly IUnitOfWork _uow;

    public SetTenantModulesHandler(ITenantRepository tenants, ITenantDirectory directory, IUnitOfWork uow)
    {
        _tenants = tenants; _directory = directory; _uow = uow;
    }

    public async Task<Result> Handle(SetTenantModulesCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null) return Result.Failure(PlatformTenants.NotFound);

        tenant.SetModules(cmd.Modules);
        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result.Success();
    }
}

public record UploadTenantBrandingCommand(int TenantId, BrandingAsset Asset, Stream Content, long Length)
    : IRequest<Result<string>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.branding.uploaded", "Tenant", TenantId.ToString(), TenantId,
        PlatformTenants.Meta(("asset", Asset.ToString())));
}

public class UploadTenantBrandingHandler : IRequestHandler<UploadTenantBrandingCommand, Result<string>>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantDirectory _directory;
    private readonly ITenantScopeRunner _scopes;
    private readonly IUnitOfWork _uow;

    public UploadTenantBrandingHandler(
        ITenantRepository tenants, ITenantDirectory directory, ITenantScopeRunner scopes, IUnitOfWork uow)
    {
        _tenants = tenants; _directory = directory; _scopes = scopes; _uow = uow;
    }

    public async Task<Result<string>> Handle(UploadTenantBrandingCommand cmd, CancellationToken ct)
    {
        var type = await BrandingFiles.InspectAsync(cmd.Asset, cmd.Content, cmd.Length, ct);
        if (!type.IsSuccess) return Result<string>.Failure(type.Error!);

        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        var store = tenant is null ? null : await _directory.FindByIdAsync(tenant.Id, ct);
        if (tenant is null || store is null) return Result<string>.Failure(PlatformTenants.NotFound);

        // التخزين داخل نطاق المتجر: الملف تحت بادئته، ويُخدَم على مضيفه وحده.
        var url = await _scopes.RunAsync<IFileStorage, string>(store,
            storage => storage.SaveAsync(cmd.Content, BrandingFiles.Folder, type.Value!.Extension, ct));
        tenant.SetBrandingAsset(cmd.Asset, url);
        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result<string>.Success(url);
    }
}

// ── دعوة مدير المتجر ─────────────────────────────────────────────────────────

// حساب TenantAdmin داخل المتجر المستهدف (نطاقه، حارس كتابته) + رابط قبول على نطاقه الأساسي — المدير
// يختار كلمة مروره ويدخل على مضيف متجره، لا على مضيف المنصّة.
public record InviteTenantAdminCommand(int TenantId, string FullName, string Email)
    : IRequest<Result<InvitationResult>>, IAuditable
{
    public AuditRecord ToAuditRecord() =>
        new("tenant.admin.invited", "User", Email?.Trim().ToLowerInvariant(), TenantId);
}

public sealed class InviteTenantAdminValidator : AbstractValidator<InviteTenantAdminCommand>
{
    public InviteTenantAdminValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
    }
}

public class InviteTenantAdminHandler : IRequestHandler<InviteTenantAdminCommand, Result<InvitationResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantDirectory _directory;
    private readonly ITenantScopeRunner _scopes;

    public InviteTenantAdminHandler(ITenantRepository tenants, ITenantDirectory directory, ITenantScopeRunner scopes)
    {
        _tenants = tenants; _directory = directory; _scopes = scopes;
    }

    public async Task<Result<InvitationResult>> Handle(InviteTenantAdminCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        var store = tenant is null ? null : await _directory.FindByIdAsync(tenant.Id, ct);
        if (tenant is null || store is null) return Result<InvitationResult>.Failure(PlatformTenants.NotFound);
        if (tenant.Status == TenantStatus.Archived)
            return Result<InvitationResult>.Failure(Error.BusinessRule("InvalidTenantOperation", "المتجر مؤرشف"));
        if (tenant.PrimaryDomain is not { } domain)
            return Result<InvitationResult>.Failure(Error.BusinessRule("TenantHasNoDomain",
                "أضف نطاقاً للمتجر قبل دعوة مديره — رابط الدعوة يُفتح على نطاق المتجر"));

        return await _scopes.RunAsync<AccountInvitations, Result<InvitationResult>>(store,
            invitations => invitations.InviteAsync(cmd.FullName, cmd.Email, Roles.TenantAdmin, tenant.Name, domain.Host, ct));
    }
}
