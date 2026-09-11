using Souq.Application.Common.Security;
using Souq.Domain.Common;

namespace Souq.Application.Tests.TestDoubles;

// مستخدم حالي قابل للضبط في الاختبارات — نفس جدول الصلاحيات الحقيقي (RolePermissions).
public sealed class TestCurrentUser : ICurrentUser
{
    private TestCurrentUser(int? userId, params string[] roles)
    {
        UserId = userId;
        Roles = roles;
    }

    public static TestCurrentUser Customer(int id) => new(id, Souq.Domain.Common.Roles.Customer);
    public static TestCurrentUser Admin(int id = 900) => new(id, Souq.Domain.Common.Roles.Admin);
    public static TestCurrentUser Anonymous() => new(null);

    public bool IsAuthenticated => UserId is not null;
    public int? UserId { get; }
    public IReadOnlyCollection<string> Roles { get; }
    public bool HasPermission(string permission) => RolePermissions.Grants(Roles, permission);
}
