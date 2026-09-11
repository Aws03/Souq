using FluentValidation;
using MediatR;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Domain.Common;
using Souq.Domain.Identity;

namespace Souq.Application.Features.Platform;

// ============================================================================
// حسابات المنصّة (صلاحية platform.users.manage — المالك وحده): قائمة، دعوة مشرف أو مالك، تفعيل/إيقاف. تعمل في
// نطاق المنصّة (مرشّح الحسابات: TenantId = NULL) — حساب متجر "غير موجود" من هنا. وسجلّ التدقيق للقراءة
// (platform.audit.view): المنصّة كلها أو متجر بعينه، والقراءة نفسها تُدقَّق.
// ============================================================================

public record ListPlatformUsersQuery(int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<AccountSummaryDto>>, IPagedQuery, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.users.listed");
}

public sealed class ListPlatformUsersQueryValidator : PagedQueryValidator<ListPlatformUsersQuery>;

public class ListPlatformUsersHandler : IRequestHandler<ListPlatformUsersQuery, PaginatedList<AccountSummaryDto>>
{
    private readonly IAccountQueries _accounts;
    public ListPlatformUsersHandler(IAccountQueries accounts) => _accounts = accounts;

    public Task<PaginatedList<AccountSummaryDto>> Handle(ListPlatformUsersQuery q, CancellationToken ct) =>
        _accounts.ListAsync(PlatformRoles.All, PageRequest.From(q), ct);
}

public record InvitePlatformUserCommand(string FullName, string Email, string Role)
    : IRequest<Result<InvitationResult>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.user.invited", "User", Email?.Trim().ToLowerInvariant(),
        Metadata: new Dictionary<string, object?> { ["role"] = Role });
}

public sealed class InvitePlatformUserValidator : AbstractValidator<InvitePlatformUserCommand>
{
    public InvitePlatformUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Role).Must(Roles.IsPlatform).WithMessage("الدور يجب أن يكون PlatformOwner أو PlatformAdmin");
    }
}

public class InvitePlatformUserHandler : IRequestHandler<InvitePlatformUserCommand, Result<InvitationResult>>
{
    private readonly AccountInvitations _invitations;
    public InvitePlatformUserHandler(AccountInvitations invitations) => _invitations = invitations;

    // رابط القبول على مضيف المنصّة نفسه (مضيف هذا الطلب).
    public Task<Result<InvitationResult>> Handle(InvitePlatformUserCommand cmd, CancellationToken ct) =>
        _invitations.InviteAsync(cmd.FullName, cmd.Email, cmd.Role, PlatformRoles.InviterName, linkHost: null, ct);
}

public record SetPlatformUserStatusCommand(int UserId, bool Active) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() =>
        new(Active ? "platform.user.enabled" : "platform.user.disabled", "User", UserId.ToString());
}

public class SetPlatformUserStatusHandler : IRequestHandler<SetPlatformUserStatusCommand, Result>
{
    private readonly AccountStatusChanger _status;
    public SetPlatformUserStatusHandler(AccountStatusChanger status) => _status = status;

    public Task<Result> Handle(SetPlatformUserStatusCommand cmd, CancellationToken ct) =>
        _status.SetActiveAsync(cmd.UserId, cmd.Active, Roles.IsPlatform, Roles.PlatformOwner, ct);
}

// Action بادئةً: "tenant." يعيد كل أفعال المتاجر. الأحدث أولاً.
public record ListAuditEntriesQuery(
    int? TenantId = null, string? Action = null, int? ActorUserId = null, DateTime? From = null, DateTime? To = null,
    int Page = 1, int PageSize = 50) : IRequest<PaginatedList<AuditEntryDto>>, IPagedQuery, IAuditable
{
    public AuditRecord ToAuditRecord() => new("platform.audit.viewed", TenantId: TenantId,
        Metadata: new Dictionary<string, object?> { ["action"] = Action, ["actorUserId"] = ActorUserId });
}

public sealed class ListAuditEntriesQueryValidator : PagedQueryValidator<ListAuditEntriesQuery>
{
    public ListAuditEntriesQueryValidator()
    {
        RuleFor(x => x.Action).MaximumLength(80);
        RuleFor(x => x).Must(q => q.From is null || q.To is null || q.From <= q.To)
            .WithMessage("بداية المدّة بعد نهايتها");
    }
}

public class ListAuditEntriesHandler : IRequestHandler<ListAuditEntriesQuery, PaginatedList<AuditEntryDto>>
{
    private readonly IPlatformQueries _queries;
    public ListAuditEntriesHandler(IPlatformQueries queries) => _queries = queries;

    public Task<PaginatedList<AuditEntryDto>> Handle(ListAuditEntriesQuery q, CancellationToken ct) =>
        _queries.ListAuditAsync(new AuditFilter(q.TenantId, q.Action?.Trim(), q.ActorUserId, q.From, q.To),
            PageRequest.From(q), ct);
}
