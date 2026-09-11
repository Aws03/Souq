using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Models;
using Souq.Domain.Identity;

namespace Souq.Infrastructure.Persistence.Queries;

// تنفيذ IAccountQueries: حسابات النطاق الحالي (مرشّح الحسابات: متجر السياق أو المنصّة) بأدوار محدّدة، الأحدث أولاً.
internal sealed class AccountQueries : IAccountQueries
{
    private readonly AppDbContext _db;
    public AccountQueries(AppDbContext db) => _db = db;

    public async Task<PaginatedList<AccountSummaryDto>> ListAsync(
        IReadOnlyCollection<string> roles, PageRequest page, CancellationToken ct) =>
        (await _db.Users.AsNoTracking()
            .Where(u => roles.Contains(u.Role))
            .OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .ToPageAsync(Row, page, ct))
        .Map(ToDto);

    // الإسقاط نفسه لقائمة المنصّة عن حسابات متجر (PlatformQueries).
    internal static readonly System.Linq.Expressions.Expression<Func<User, AccountRow>> Row = u => new AccountRow(
        u.Id, u.FullName, u.Email, u.Role, u.Status, u.PasswordHash == "", u.EmailConfirmedAt != null, u.LastLoginAt, u.CreatedAt);

    internal static AccountSummaryDto ToDto(AccountRow r) => new(
        r.Id, r.FullName, r.Email, r.Role, r.Status.ToString(), r.InvitationPending, r.EmailConfirmed, r.LastLoginAt, r.CreatedAt);

    internal sealed record AccountRow(
        int Id, string FullName, string Email, string Role, UserStatus Status,
        bool InvitationPending, bool EmailConfirmed, DateTime? LastLoginAt, DateTime CreatedAt);
}
