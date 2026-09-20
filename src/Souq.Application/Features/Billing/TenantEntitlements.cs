using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Billing;

// ============================================================================
// استحقاقات متجر بعينه (C1، ADR-0047 §4): إسناد خطة، ومنح استثناء دعم مؤقّت، وسحبه.
//
// كل أمر هنا يستدعي ITenantDirectory.Invalidate بعد الحفظ — لأن الوحدات الفعّالة تُحسب داخل
// لقطة الدليل، فتغييرُ ما يُحسب منه بلا إبطال يعني متجراً يعمل بعقده القديم حتى دقيقة.
// (وانتهاء استثناء **بنفسه** لا يستدعيه أحد: تأخّره حتى 60 ثانية مذكور في TenantDirectoryCache.)
// ============================================================================

public record GetTenantEntitlementsQuery(int TenantId) : IRequest<Result<TenantEntitlementsDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.tenant.entitlements.viewed", "Tenant", TenantId.ToString(), TenantId);
}

public class GetTenantEntitlementsHandler : IRequestHandler<GetTenantEntitlementsQuery, Result<TenantEntitlementsDto>>
{
    private readonly IBillingQueries _queries;
    public GetTenantEntitlementsHandler(IBillingQueries queries) => _queries = queries;

    public async Task<Result<TenantEntitlementsDto>> Handle(GetTenantEntitlementsQuery q, CancellationToken ct) =>
        await _queries.GetTenantEntitlementsAsync(q.TenantId, ct) is { } entitlements
            ? Result<TenantEntitlementsDto>.Success(entitlements)
            : Result<TenantEntitlementsDto>.Failure(BillingErrors.TenantNotFound);
}

// ── إسناد الخطة ─────────────────────────────────────────────────────────────

// صفّ اشتراك واحد لكل متجر: الإسناد ينشئه أو يحوّله. لا يُسنَد إلا إصدار **منشور** (يرفضه المجال).
public record AssignTenantPlanCommand(int TenantId, int PlanId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.tenant.plan.assigned", "Tenant", TenantId.ToString(), TenantId,
        BillingErrors.Meta(("planId", PlanId)));
}

public class AssignTenantPlanHandler : IRequestHandler<AssignTenantPlanCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly IPlanRepository _plans;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ITenantDirectory _directory;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public AssignTenantPlanHandler(
        ITenantRepository tenants, IPlanRepository plans, ISubscriptionRepository subscriptions,
        ITenantDirectory directory, TimeProvider clock, IUnitOfWork uow)
    {
        _tenants = tenants; _plans = plans; _subscriptions = subscriptions;
        _directory = directory; _clock = clock; _uow = uow;
    }

    public async Task<Result> Handle(AssignTenantPlanCommand cmd, CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(cmd.TenantId, ct) is null) return Result.Failure(BillingErrors.TenantNotFound);

        var plan = await _plans.GetByIdAsync(cmd.PlanId, ct);
        if (plan is null) return Result.Failure(BillingErrors.PlanNotFound);

        var now = _clock.GetUtcNow().UtcDateTime;
        var existing = await _subscriptions.FindByTenantAsync(cmd.TenantId, ct);
        if (existing is null)
            await _subscriptions.AddAsync(new Subscription(cmd.TenantId, plan, now), ct);
        else
            existing.ChangePlan(plan, now);

        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result.Success();
    }
}

// الإلغاء يوقف ما يمنحه العقد فوراً: المتجر يبقى قائماً وتُطفأ وحداته الاختيارية. **لا يوقف المتجر**
// — إيقاف المتجر حالةٌ أخرى بقرارها (C-17) ومساره الخاص (ChangeTenantStatus).
public record CancelTenantPlanCommand(int TenantId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.tenant.plan.cancelled", "Tenant", TenantId.ToString(), TenantId);
}

public class CancelTenantPlanHandler : IRequestHandler<CancelTenantPlanCommand, Result>
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ITenantDirectory _directory;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public CancelTenantPlanHandler(
        ISubscriptionRepository subscriptions, ITenantDirectory directory, TimeProvider clock, IUnitOfWork uow)
    {
        _subscriptions = subscriptions; _directory = directory; _clock = clock; _uow = uow;
    }

    public async Task<Result> Handle(CancelTenantPlanCommand cmd, CancellationToken ct)
    {
        var subscription = await _subscriptions.FindByTenantAsync(cmd.TenantId, ct);
        if (subscription is null) return Result.Failure(BillingErrors.TenantNotFound);

        subscription.Cancel(_clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result.Success();
    }
}

// ── استثناءات الدعم ─────────────────────────────────────────────────────────

public record ListEntitlementOverridesQuery(int TenantId)
    : IRequest<Result<IReadOnlyList<EntitlementOverrideDto>>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.tenant.overrides.viewed", "Tenant", TenantId.ToString(), TenantId);
}

public class ListEntitlementOverridesHandler
    : IRequestHandler<ListEntitlementOverridesQuery, Result<IReadOnlyList<EntitlementOverrideDto>>>
{
    private readonly IBillingQueries _queries;
    private readonly ITenantDirectory _directory;

    public ListEntitlementOverridesHandler(IBillingQueries queries, ITenantDirectory directory)
    {
        _queries = queries; _directory = directory;
    }

    public async Task<Result<IReadOnlyList<EntitlementOverrideDto>>> Handle(
        ListEntitlementOverridesQuery q, CancellationToken ct)
    {
        if (await _directory.FindByIdAsync(q.TenantId, ct) is null)
            return Result<IReadOnlyList<EntitlementOverrideDto>>.Failure(BillingErrors.TenantNotFound);
        return Result<IReadOnlyList<EntitlementOverrideDto>>.Success(await _queries.ListOverridesAsync(q.TenantId, ct));
    }
}

// منحُ قدرة خارج الخطة — الخيار (ب) في قرار المالك C-14، وهو ما تسمّيه خطة C1 صراحةً ضمن
// مُخرَجاتها: ينتهي بنفسه، ومنسوب لحساب مانحه، ومُدقَّق. إن أجاب المالك "لا" فما يُحذف هو هذا
// الملفّ وجدوله وشاشته — ولا شيء آخر يعتمد عليه.
public record GrantEntitlementOverrideCommand(int TenantId, string Entitlement, int Days, string Reason)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.tenant.override.granted", "Tenant", TenantId.ToString(), TenantId,
        BillingErrors.Meta(("entitlement", Entitlement), ("days", Days), ("reason", Reason)));
}

public sealed class GrantEntitlementOverrideValidator : AbstractValidator<GrantEntitlementOverrideCommand>
{
    public GrantEntitlementOverrideValidator()
    {
        RuleFor(x => x.Entitlement).NotEmpty();
        RuleFor(x => x.Days).InclusiveBetween(1, EntitlementOverride.MaxDurationDays);
        RuleFor(x => x.Reason).NotEmpty()
            .MinimumLength(EntitlementOverride.ReasonMinLength)
            .MaximumLength(EntitlementOverride.ReasonMaxLength);
    }
}

public class GrantEntitlementOverrideHandler : IRequestHandler<GrantEntitlementOverrideCommand, Result<int>>
{
    private readonly ITenantRepository _tenants;
    private readonly IEntitlementOverrideRepository _overrides;
    private readonly ITenantDirectory _directory;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public GrantEntitlementOverrideHandler(
        ITenantRepository tenants, IEntitlementOverrideRepository overrides, ITenantDirectory directory,
        ICurrentUser user, TimeProvider clock, IUnitOfWork uow)
    {
        _tenants = tenants; _overrides = overrides; _directory = directory;
        _user = user; _clock = clock; _uow = uow;
    }

    public async Task<Result<int>> Handle(GrantEntitlementOverrideCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null) return Result<int>.Failure(BillingErrors.TenantNotFound);

        var now = _clock.GetUtcNow().UtcDateTime;
        var key = Entitlements.Normalize(cmd.Entitlement);

        // الاستثناء يوسّع ما يسمح به **العقد**، والوحدة المطفأة تشغيلياً تبقى مطفأة (الفعّال تقاطعٌ).
        // فمنحُ استثناء لوحدة أطفأتها المنصّة لا يفعل شيئاً — ولا شيء أسوأ من أمرٍ ينجح ولا يفعل
        // شيئاً: يُرفض صراحةً بدل أن يُكتب صفٌّ ميت ويُقال للمشغّل إنه نجح.
        if (!tenant.Modules.Contains(key))
            return Result<int>.Failure(Error.Conflict("ModuleSwitchedOff",
                "هذه الوحدة مُطفأة لهذا المتجر — فعّلها أولاً، فالاستثناء يوسّع الخطة لا يتجاوز مفتاح المنصّة"));

        // استثناءان ساريان على استحقاق واحد يجعلان "متى ينتهي هذا؟" بلا جواب واحد.
        if (await _overrides.FindActiveAsync(cmd.TenantId, key, now, ct) is not null)
            return Result<int>.Failure(Error.Conflict("OverrideAlreadyActive", "لهذا الاستحقاق استثناء سارٍ — اسحبه أولاً"));

        var granted = new EntitlementOverride(cmd.TenantId, key, now.AddDays(cmd.Days), _user.RequireUserId(), cmd.Reason, now);
        await _overrides.AddAsync(granted, ct);
        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result<int>.Success(granted.Id);
    }
}

public record RevokeEntitlementOverrideCommand(int TenantId, int OverrideId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.tenant.override.revoked", "Tenant", TenantId.ToString(), TenantId,
        BillingErrors.Meta(("overrideId", OverrideId)));
}

public class RevokeEntitlementOverrideHandler : IRequestHandler<RevokeEntitlementOverrideCommand, Result>
{
    private readonly IEntitlementOverrideRepository _overrides;
    private readonly ITenantDirectory _directory;
    private readonly TimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RevokeEntitlementOverrideHandler(
        IEntitlementOverrideRepository overrides, ITenantDirectory directory, TimeProvider clock, IUnitOfWork uow)
    {
        _overrides = overrides; _directory = directory; _clock = clock; _uow = uow;
    }

    public async Task<Result> Handle(RevokeEntitlementOverrideCommand cmd, CancellationToken ct)
    {
        var granted = await _overrides.GetByIdAsync(cmd.OverrideId, ct);

        // المتجر في المسار جزء من الهوية لا زينة: استثناء متجر آخر لا يُسحب من صفحة هذا المتجر.
        if (granted is null || granted.TenantId != cmd.TenantId) return Result.Failure(BillingErrors.OverrideNotFound);

        granted.Revoke(_clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        _directory.Invalidate();
        return Result.Success();
    }
}
