using FluentValidation;
using MediatR;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Identity;

namespace Souq.Application.Features.Staff;

// ============================================================================
// موظّفو المتجر (وحدة Identity — صلاحية store.staff.manage، أي مدير المتجر): قائمة، دعوة مدير أو موظّف،
// تفعيل/إيقاف. كل شيء في متجر السياق عبر مرشّح الحسابات — معرّف حساب في متجر آخر "غير موجود" من هنا.
// حسابات العملاء ليست هنا (وحدة Customers، المرحلة 7).
// ============================================================================

public static class StaffRoles
{
    public static readonly IReadOnlyCollection<string> All = [Roles.TenantAdmin, Roles.TenantStaff];
}

public record ListStaffQuery(int Page = 1, int PageSize = 20) : IRequest<PaginatedList<AccountSummaryDto>>, IPagedQuery;

public sealed class ListStaffQueryValidator : PagedQueryValidator<ListStaffQuery>;

public class ListStaffHandler : IRequestHandler<ListStaffQuery, PaginatedList<AccountSummaryDto>>
{
    private readonly IAccountQueries _accounts;
    public ListStaffHandler(IAccountQueries accounts) => _accounts = accounts;

    public Task<PaginatedList<AccountSummaryDto>> Handle(ListStaffQuery query, CancellationToken ct) =>
        _accounts.ListAsync(StaffRoles.All, PageRequest.From(query), ct);
}

public record InviteStaffCommand(string FullName, string Email, string Role) : IRequest<Result<InvitationResult>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("store.staff.invited", "User", Email?.Trim().ToLowerInvariant(),
        Metadata: new Dictionary<string, object?> { ["role"] = Role });
}

public sealed class InviteStaffValidator : AbstractValidator<InviteStaffCommand>
{
    public InviteStaffValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Role).Must(Roles.IsStoreStaff).WithMessage("الدور يجب أن يكون TenantAdmin أو TenantStaff");
    }
}

public class InviteStaffHandler : IRequestHandler<InviteStaffCommand, Result<InvitationResult>>
{
    private readonly AccountInvitations _invitations;
    private readonly ITenantContext _context;

    public InviteStaffHandler(AccountInvitations invitations, ITenantContext context)
    {
        _invitations = invitations; _context = context;
    }

    // رابط القبول على مضيف هذا الطلب نفسه — مضيف المتجر الذي يديره المدير الآن.
    public Task<Result<InvitationResult>> Handle(InviteStaffCommand cmd, CancellationToken ct) =>
        _invitations.InviteAsync(cmd.FullName, cmd.Email, cmd.Role, _context.RequireTenant().Name, linkHost: null, ct);
}

public record SetStaffStatusCommand(int UserId, bool Active) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() =>
        new(Active ? "store.staff.enabled" : "store.staff.disabled", "User", UserId.ToString());
}

public class SetStaffStatusHandler : IRequestHandler<SetStaffStatusCommand, Result>
{
    private readonly AccountStatusChanger _status;
    public SetStaffStatusHandler(AccountStatusChanger status) => _status = status;

    public Task<Result> Handle(SetStaffStatusCommand cmd, CancellationToken ct) =>
        _status.SetActiveAsync(cmd.UserId, cmd.Active, Roles.IsStoreStaff, Roles.TenantAdmin, ct);
}
