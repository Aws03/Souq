using FluentValidation;
using MediatR;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Billing.Contracts;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

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
    private readonly IUserRepository _users;
    private readonly ITenantQuotaGuard _quota;
    private readonly ITenantContext _context;
    private readonly IUnitOfWork _uow;

    public InviteStaffHandler(
        AccountInvitations invitations, IUserRepository users, ITenantQuotaGuard quota,
        ITenantContext context, IUnitOfWork uow)
    {
        _invitations = invitations; _users = users; _quota = quota; _context = context; _uow = uow;
    }

    // رابط القبول على مضيف هذا الطلب نفسه — مضيف المتجر الذي يديره المدير الآن.
    //
    // الحصّة هنا لا في AccountInvitations: ذاك المسار يخدم دعوات **المنصّة** أيضاً، ولا متجر لها
    // ولا مقاعد. ولا يُحجز إلّا لحساب جديد فعلاً — تجديدُ دعوةٍ معلّقة يخصّ حساباً قائماً معدوداً،
    // وحجزُه كان سيستهلك مقعداً بكل إعادة إرسال.
    public Task<Result<InvitationResult>> Handle(InviteStaffCommand cmd, CancellationToken ct) =>
        _uow.InTransactionAsync(async () =>
        {
            if (await _users.GetByEmailAsync(cmd.Email, ct) is null)
            {
                var decision = await _quota.ReserveAsync(LimitNames.StaffSeats, ct);
                if (!decision.Allowed) return Result<InvitationResult>.Failure(decision.ToError());
            }

            return await _invitations.InviteAsync(
                cmd.FullName, cmd.Email, cmd.Role, _context.RequireTenant().Name, linkHost: null, ct);
        }, ct);
}

public record SetStaffStatusCommand(int UserId, bool Active) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() =>
        new(Active ? "store.staff.enabled" : "store.staff.disabled", "User", UserId.ToString());
}

// ============================================================================
// المقعد يُشغَل بالتفعيل ويُفرَغ بالإيقاف (C2): لا مسار حذفٍ لحساب موظّف أصلاً، فالإيقاف هو
// الطريقة الوحيدة التي يملكها التاجر لتفريغ مقعد — ولو لم تُفرِغه لصار الحدّ سقّاطة (LimitNames).
// والحالة تُقرأ قبل التصرّف: تفعيلُ المفعَّل ليس مقعداً جديداً، وإيقافُ الموقَف ليس إفراغاً.
// ============================================================================
public class SetStaffStatusHandler : IRequestHandler<SetStaffStatusCommand, Result>
{
    private readonly AccountStatusChanger _status;
    private readonly IUserRepository _users;
    private readonly ITenantQuotaGuard _quota;
    private readonly IUnitOfWork _uow;

    public SetStaffStatusHandler(
        AccountStatusChanger status, IUserRepository users, ITenantQuotaGuard quota, IUnitOfWork uow)
    {
        _status = status; _users = users; _quota = quota; _uow = uow;
    }

    public async Task<Result> Handle(SetStaffStatusCommand cmd, CancellationToken ct)
    {
        // حسابٌ مجهول أو خارج الأدوار المُدارة: لا نُقرّر هنا — AccountStatusChanger يجيب "غير موجود"،
        // وتكرارُ الحكم هنا كان يعني جوابين لسؤال واحد.
        var user = await _users.GetByIdAsync(cmd.UserId, ct);
        var managed = user is not null && Roles.IsStoreStaff(user.Role);
        var wasCounted = managed && user!.Status == UserStatus.Active;
        var willBeCounted = managed && cmd.Active;

        if (!wasCounted && willBeCounted)
            return await _uow.InTransactionAsync(async () =>
            {
                var decision = await _quota.ReserveAsync(LimitNames.StaffSeats, ct);
                if (!decision.Allowed) return Result.Failure(decision.ToError());

                var result = await _status.SetActiveAsync(cmd.UserId, cmd.Active, Roles.IsStoreStaff, Roles.TenantAdmin, ct);

                // رفضٌ يُعيد النتيجة لا استثناءً، فالمعاملة تلتزم: نعوّض داخلها فيتصافى الأثر.
                if (!result.IsSuccess) await _quota.ReleaseAsync(LimitNames.StaffSeats, ct: ct);
                return result;
            }, ct);

        var changed = await _status.SetActiveAsync(cmd.UserId, cmd.Active, Roles.IsStoreStaff, Roles.TenantAdmin, ct);
        if (changed.IsSuccess && wasCounted && !willBeCounted)
            await _quota.ReleaseAsync(LimitNames.StaffSeats, ct: ct);
        return changed;
    }
}
