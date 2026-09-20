using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Billing;

// ============================================================================
// كتالوج الخطط (C1، ADR-0047 §4): إنشاء إصدار، نشره، تقاعده. الشرائح التجارية نفسها — كم شريحة
// وما حدودها وهل الحدّ صلب أم ليّن — قرار المالك C-12، فما يُبنى هنا هو **الآلية** التي تصحّ تحت
// كل إجابة، لا إجابة مُختارة نيابةً عنه. والخطط لا أسعار لها بعد: التسعير والتحصيل من C5.
// ============================================================================

internal static class BillingErrors
{
    public static Error PlanNotFound => Error.NotFound("الخطة غير موجودة");
    public static Error TenantNotFound => Error.NotFound("المتجر غير موجود");
    public static Error OverrideNotFound => Error.NotFound("الاستثناء غير موجود");

    public static Dictionary<string, object?> Meta(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}

// ── القراءة ─────────────────────────────────────────────────────────────────

public record ListPlansQuery(string? Search = null, bool IncludeRetired = false, int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<PlanSummaryDto>>, IPagedQuery, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.plans.listed");
}

public sealed class ListPlansQueryValidator : PagedQueryValidator<ListPlansQuery>
{
    public ListPlansQueryValidator() => RuleFor(x => x.Search).MaximumLength(100);
}

public class ListPlansHandler : IRequestHandler<ListPlansQuery, PaginatedList<PlanSummaryDto>>
{
    private readonly IBillingQueries _queries;
    public ListPlansHandler(IBillingQueries queries) => _queries = queries;

    public Task<PaginatedList<PlanSummaryDto>> Handle(ListPlansQuery q, CancellationToken ct) =>
        _queries.ListPlansAsync(new PlanListFilter(q.Search?.Trim(), q.IncludeRetired), PageRequest.From(q), ct);
}

public record GetPlanQuery(int PlanId) : IRequest<Result<PlanDetailDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.plan.viewed", "Plan", PlanId.ToString());
}

public class GetPlanHandler : IRequestHandler<GetPlanQuery, Result<PlanDetailDto>>
{
    private readonly IBillingQueries _queries;
    public GetPlanHandler(IBillingQueries queries) => _queries = queries;

    public async Task<Result<PlanDetailDto>> Handle(GetPlanQuery q, CancellationToken ct) =>
        await _queries.GetPlanAsync(q.PlanId, ct) is { } plan
            ? Result<PlanDetailDto>.Success(plan)
            : Result<PlanDetailDto>.Failure(BillingErrors.PlanNotFound);
}

// ── الكتابة ─────────────────────────────────────────────────────────────────

// إصدار جديد لمعرّف خطة (الأول = 1). يبدأ مسوّدةً: لا يُشترَك عليه حتى يُنشر.
public record CreatePlanVersionCommand(
    string Code, string Name, IReadOnlyList<string> Entitlements, IReadOnlyList<PlanLimitDto> Limits)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.plan.version.created", "Plan", Code?.Trim().ToLowerInvariant(),
        Metadata: BillingErrors.Meta(("name", Name), ("entitlements", Entitlements)));
}

public sealed class CreatePlanVersionValidator : AbstractValidator<CreatePlanVersionCommand>
{
    public CreatePlanVersionValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Plan.CodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Plan.NameMaxLength);
        RuleFor(x => x.Entitlements).NotNull();
        RuleFor(x => x.Limits).NotNull();
    }
}

public class CreatePlanVersionHandler : IRequestHandler<CreatePlanVersionCommand, Result<int>>
{
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _uow;

    public CreatePlanVersionHandler(IPlanRepository plans, IUnitOfWork uow)
    {
        _plans = plans; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreatePlanVersionCommand cmd, CancellationToken ct)
    {
        var code = Plan.NormalizeCode(cmd.Code);
        var plan = new Plan(code, await _plans.NextVersionAsync(code, ct), cmd.Name);
        plan.SetEntitlements(cmd.Entitlements);
        plan.SetLimits(cmd.Limits.Select(l => new Limit(l.Name, l.Value)));

        await _plans.AddAsync(plan, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(plan.Id);
    }
}

// تحرير مسوّدة. لا يمسّ المنشور: المشترك يحتفظ بالشروط التي اشترك عليها (يرفضه المجال).
public record UpdatePlanDraftCommand(
    int PlanId, string Name, IReadOnlyList<string> Entitlements, IReadOnlyList<PlanLimitDto> Limits)
    : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.plan.draft.updated", "Plan", PlanId.ToString(),
        Metadata: BillingErrors.Meta(("name", Name), ("entitlements", Entitlements)));
}

public sealed class UpdatePlanDraftValidator : AbstractValidator<UpdatePlanDraftCommand>
{
    public UpdatePlanDraftValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Plan.NameMaxLength);
        RuleFor(x => x.Entitlements).NotNull();
        RuleFor(x => x.Limits).NotNull();
    }
}

public class UpdatePlanDraftHandler : IRequestHandler<UpdatePlanDraftCommand, Result>
{
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _uow;

    public UpdatePlanDraftHandler(IPlanRepository plans, IUnitOfWork uow)
    {
        _plans = plans; _uow = uow;
    }

    public async Task<Result> Handle(UpdatePlanDraftCommand cmd, CancellationToken ct)
    {
        var plan = await _plans.GetWithTermsAsync(cmd.PlanId, ct);
        if (plan is null) return Result.Failure(BillingErrors.PlanNotFound);

        plan.Rename(cmd.Name);
        plan.SetEntitlements(cmd.Entitlements);
        plan.SetLimits(cmd.Limits.Select(l => new Limit(l.Name, l.Value)));
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// النشر يُجمّد الشروط ويفتح الاشتراك؛ التقاعد يمنع اشتراكاً جديداً ولا يمسّ المشتركين القائمين.
public record PublishPlanCommand(int PlanId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.plan.published", "Plan", PlanId.ToString());
}

public class PublishPlanHandler : IRequestHandler<PublishPlanCommand, Result>
{
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _uow;

    public PublishPlanHandler(IPlanRepository plans, IUnitOfWork uow)
    {
        _plans = plans; _uow = uow;
    }

    public async Task<Result> Handle(PublishPlanCommand cmd, CancellationToken ct)
    {
        var plan = await _plans.GetByIdAsync(cmd.PlanId, ct);
        if (plan is null) return Result.Failure(BillingErrors.PlanNotFound);

        plan.Publish();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public record RetirePlanCommand(int PlanId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.plan.retired", "Plan", PlanId.ToString());
}

public class RetirePlanHandler : IRequestHandler<RetirePlanCommand, Result>
{
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _uow;

    public RetirePlanHandler(IPlanRepository plans, IUnitOfWork uow)
    {
        _plans = plans; _uow = uow;
    }

    public async Task<Result> Handle(RetirePlanCommand cmd, CancellationToken ct)
    {
        var plan = await _plans.GetByIdAsync(cmd.PlanId, ct);
        if (plan is null) return Result.Failure(BillingErrors.PlanNotFound);

        plan.Retire();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
