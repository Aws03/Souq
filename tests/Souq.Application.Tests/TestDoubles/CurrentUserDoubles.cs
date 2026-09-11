using Souq.Application.Common.Security;
using Souq.Domain.Common;

namespace Souq.Application.Tests.TestDoubles;

// مستخدم حالي قابل للضبط في الاختبارات — نفس جدول الصلاحيات الحقيقي (RolePermissions). الحساب
// (UserId) منفصل عن ملف الشراء (CustomerId) منذ المرحلة 3: الموظّف والمدير بلا ملف عميل.
public sealed class TestCurrentUser : ICurrentUser
{
    private TestCurrentUser(int? userId, int? customerId, params string[] roles)
    {
        UserId = userId;
        CustomerId = customerId;
        Roles = roles;
    }

    public static TestCurrentUser Customer(int customerId) => new(customerId, customerId, Souq.Domain.Common.Roles.Customer);
    public static TestCurrentUser Admin(int userId = 900) => new(userId, null, Souq.Domain.Common.Roles.TenantAdmin);
    public static TestCurrentUser Staff(int userId = 901) => new(userId, null, Souq.Domain.Common.Roles.TenantStaff);
    public static TestCurrentUser Anonymous() => new(null, null);

    public bool IsAuthenticated => UserId is not null;
    public int? UserId { get; }
    public int? CustomerId { get; }
    public IReadOnlyCollection<string> Roles { get; }
    public bool HasPermission(string permission) => RolePermissions.Grants(Roles, permission);
}
