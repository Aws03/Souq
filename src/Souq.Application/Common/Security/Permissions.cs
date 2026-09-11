using Souq.Domain.Common;

namespace Souq.Application.Common.Security;

// ============================================================================
// الصلاحيات — ثوابت في الكود مجمّعة حسب الوحدة (ADR-0019). الـ Controllers تعلن الصلاحية
// المطلوبة ([HasPermission]) لا اسم الدور، وحالات الاستخدام تسأل ICurrentUser.HasPermission.
// إضافة دور (TenantStaff بصلاحيات جزئية في المرحلة 3، PlatformOwner في المرحلة 4) = سطر في
// RolePermissions، لا تعديل عشرات السمات. الأدوار المخصّصة لكل متجر مؤجّلة حتى يطلبها عميل.
// ============================================================================
public static class Permissions
{
    public static class Catalog
    {
        public const string Manage = "catalog.manage";         // المنتجات والفئات والوسائط
    }

    public static class Inventory
    {
        public const string View = "inventory.view";           // الجرد وسجلّ الحركة
    }

    public static class Orders
    {
        public const string Manage = "orders.manage";          // كل الطلبات: عرض وتغيير الحالة
    }

    public static class Promotions
    {
        public const string Manage = "promotions.manage";      // الكوبونات
    }

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Catalog.Manage, Inventory.View, Orders.Manage, Promotions.Manage,
    };
}

// مصدر الحقيقة الوحيد: أيّ دور يمنح أيّ صلاحية. قدرات العميل ملكيّة (طلباته، تقييماته)
// تُفحص بـ CanAccessOwnedBy، لا صلاحيات — فالعميل لا يملك أي صلاحية إدارية.
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByRole =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Roles.Admin] = Permissions.All,
            [Roles.Customer] = new HashSet<string>(),
        };

    public static bool Grants(IEnumerable<string> roles, string permission) =>
        roles.Any(role => ByRole.TryGetValue(role, out var granted) && granted.Contains(permission));

    public static IReadOnlySet<string> For(string role) =>
        ByRole.TryGetValue(role, out var granted) ? granted : new HashSet<string>();
}
