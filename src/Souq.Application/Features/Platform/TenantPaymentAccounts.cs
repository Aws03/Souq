using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Payments.Contracts;

namespace Souq.Application.Features.Platform;

// ============================================================================
// حساب بوّابة متجر من منطقة المنصّة (المرحلة 11، platform.tenants.manage): المحرّر الفعلي يملكه Payments
// (Features/Payments/StorePaymentAccounts.cs)؛ منطقة المنصّة تدخل نطاق المتجر المستهدف عبر ITenantScopeRunner
// وتطلب IStorePaymentAccountEditor وحده من Payments.Contracts — عقد منشور لا صنف وحدة أخرى مباشرة
// (ModuleBoundaryAudit.md، التدقيق المعماري M1 — TD-04/R-04). التشفير مربوط بذلك المتجر وحارس الكتابة يختمه،
// لا تجاوز لمرشّح المستأجر. الأمر يبقى هنا لا في Payments لأنه يحمل TenantId من مسار المنصّة، وحدها
// Features.Platform يُباح لطلباتها ذلك (MultiTenancy.md §2).
// ============================================================================
// قراءة المنصّة لبيانات متجر مُدقَّقة كغيرها من طلبات المنطقة (ما يُعرض: التلميح لا السرّ).
public record GetTenantPaymentAccountQuery(int TenantId) : IRequest<Result<StorePaymentAccountDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.payments.viewed", "Tenant", TenantId.ToString(), TenantId);
}

public class GetTenantPaymentAccountHandler : IRequestHandler<GetTenantPaymentAccountQuery, Result<StorePaymentAccountDto>>
{
    private readonly ITenantDirectory _directory;
    private readonly ITenantScopeRunner _scopes;

    public GetTenantPaymentAccountHandler(ITenantDirectory directory, ITenantScopeRunner scopes)
    {
        _directory = directory; _scopes = scopes;
    }

    public async Task<Result<StorePaymentAccountDto>> Handle(GetTenantPaymentAccountQuery query, CancellationToken ct)
    {
        var store = await _directory.FindByIdAsync(query.TenantId, ct);
        if (store is null) return Result<StorePaymentAccountDto>.Failure(PlatformTenants.NotFound);
        return Result<StorePaymentAccountDto>.Success(
            await _scopes.RunAsync<IStorePaymentAccountEditor, StorePaymentAccountDto>(store, editor => editor.GetAsync(ct)));
    }
}

public record UpdateTenantPaymentAccountCommand(int TenantId, StorePaymentAccountInput Account) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.payments.updated", "Tenant", TenantId.ToString(), TenantId,
        StorePaymentAudit.Meta(Account));
}

public sealed class UpdateTenantPaymentAccountValidator : AbstractValidator<UpdateTenantPaymentAccountCommand>
{
    public UpdateTenantPaymentAccountValidator()
    {
        RuleFor(x => x.TenantId).GreaterThan(0);
        RuleFor(x => x.Account).NotNull().SetValidator(new StorePaymentAccountInputValidator());
    }
}

public class UpdateTenantPaymentAccountHandler : IRequestHandler<UpdateTenantPaymentAccountCommand, Result>
{
    private readonly ITenantDirectory _directory;
    private readonly ITenantScopeRunner _scopes;

    public UpdateTenantPaymentAccountHandler(ITenantDirectory directory, ITenantScopeRunner scopes)
    {
        _directory = directory; _scopes = scopes;
    }

    public async Task<Result> Handle(UpdateTenantPaymentAccountCommand cmd, CancellationToken ct)
    {
        var store = await _directory.FindByIdAsync(cmd.TenantId, ct);
        if (store is null) return Result.Failure(PlatformTenants.NotFound);
        return await _scopes.RunAsync<IStorePaymentAccountEditor, Result>(store, editor => editor.SaveAsync(cmd.Account, ct));
    }
}

public record RemoveTenantPaymentAccountCommand(int TenantId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("tenant.payments.removed", "Tenant", TenantId.ToString(), TenantId);
}

public class RemoveTenantPaymentAccountHandler : IRequestHandler<RemoveTenantPaymentAccountCommand, Result>
{
    private readonly ITenantDirectory _directory;
    private readonly ITenantScopeRunner _scopes;

    public RemoveTenantPaymentAccountHandler(ITenantDirectory directory, ITenantScopeRunner scopes)
    {
        _directory = directory; _scopes = scopes;
    }

    public async Task<Result> Handle(RemoveTenantPaymentAccountCommand cmd, CancellationToken ct)
    {
        var store = await _directory.FindByIdAsync(cmd.TenantId, ct);
        if (store is null) return Result.Failure(PlatformTenants.NotFound);
        return await _scopes.RunAsync<IStorePaymentAccountEditor, Result>(store, editor => editor.RemoveAsync(ct));
    }
}
