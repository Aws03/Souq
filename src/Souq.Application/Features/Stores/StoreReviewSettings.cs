using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Stores;

// ============================================================================
// سياسة نشر التقييمات لمتجر السياق (المرحلة 13، ADR-0033): اعتماد تلقائي أو إشراف مسبق. إعداد متجر (وحدة Platform) لا حالة
// تقييم — يقرأه المشرف (reviews.moderate) ويغيّره من يدير إعدادات المتجر أيضاً. لا معرّف متجر في الطلب: متجر المضيف دائماً.
// ============================================================================

public record ReviewSettingsDto(bool AutoApprove);

public record GetReviewSettingsQuery : IRequest<ReviewSettingsDto>;

public class GetReviewSettingsHandler : IRequestHandler<GetReviewSettingsQuery, ReviewSettingsDto>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantContext _context;

    public GetReviewSettingsHandler(ITenantRepository tenants, ITenantContext context)
    {
        _tenants = tenants; _context = context;
    }

    public async Task<ReviewSettingsDto> Handle(GetReviewSettingsQuery query, CancellationToken ct) =>
        new((await GetStoreSettingsHandler.StoreOf(_tenants, _context, ct)).ReviewsAutoApprove);
}

public record UpdateReviewSettingsCommand(bool AutoApprove) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("store.reviews.updated", "Tenant",
        Metadata: new Dictionary<string, object?> { ["autoApprove"] = AutoApprove });
}

public class UpdateReviewSettingsHandler : IRequestHandler<UpdateReviewSettingsCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantContext _context;
    private readonly IUnitOfWork _uow;

    public UpdateReviewSettingsHandler(ITenantRepository tenants, ITenantContext context, IUnitOfWork uow)
    {
        _tenants = tenants; _context = context; _uow = uow;
    }

    public async Task<Result> Handle(UpdateReviewSettingsCommand cmd, CancellationToken ct)
    {
        var tenant = await GetStoreSettingsHandler.StoreOf(_tenants, _context, ct);
        tenant.SetReviewsAutoApprove(cmd.AutoApprove);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
