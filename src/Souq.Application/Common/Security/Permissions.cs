using Souq.Domain.Common;

namespace Souq.Application.Common.Security;

// ============================================================================
// الصلاحيات — ثوابت في الكود مجمّعة حسب الوحدة (ADR-0010/0019). الـ Controllers تعلن الصلاحية
// المطلوبة ([HasPermission]) لا اسم الدور، وحالات الاستخدام تسأل ICurrentUser.HasPermission.
// عالمان منفصلان: صلاحيات المتجر (تُمنح لأدوار المتجر فقط) وصلاحيات المنصّة (لأدوار المنصّة فقط) —
// والتوكن نفسه مربوط بمضيفه، فلا تُستعمل صلاحية منصّة على مضيف متجر ولا العكس.
// الأدوار المخصّصة لكل متجر مؤجّلة حتى يطلبها عميل (الجدول هنا يتّسع لها بلا تغيير في النقاط).
// ============================================================================
public static class Permissions
{
    public static class Catalog
    {
        public const string Manage = "catalog.manage";            // المنتجات والفئات والوسائط
    }

    public static class Inventory
    {
        public const string View = "inventory.view";              // الجرد وسجلّ الحركة
        public const string Manage = "inventory.manage";          // التعديلات والحدود (المرحلة 6)
    }

    public static class Orders
    {
        public const string View = "orders.view";                 // كل طلبات المتجر قراءةً
        public const string Manage = "orders.manage";             // تغيير الحالة، الإلغاء، التأكيد
    }

    public static class Customers
    {
        public const string View = "customers.view";              // قائمة العملاء وتفاصيلهم (المرحلة 7)
        public const string Manage = "customers.manage";          // حظر، تصدير، حذف (المرحلة 7)
    }

    public static class Promotions
    {
        public const string Manage = "promotions.manage";         // الكوبونات
    }

    public static class Reviews
    {
        public const string Moderate = "reviews.moderate";        // الإشراف على التقييمات (المرحلة 13)
    }

    public static class Store
    {
        public const string Settings = "store.settings.manage";   // هوية المتجر ومحتواه (المرحلة 4)
        public const string Staff = "store.staff.manage";         // موظّفو المتجر
        public const string Reports = "store.reports.view";       // لوحة المؤشّرات (المرحلة 17)
        public const string Payments = "store.payments.manage";   // إعدادات الدفع والاسترداد (المرحلة 11)
        public const string Shipping = "store.shipping.manage";   // طرق الشحن (المرحلة 12)
    }

    public static class Platform
    {
        public const string Tenants = "platform.tenants.manage";  // إنشاء المتاجر وتشغيلها ونطاقاتها
        public const string Users = "platform.users.manage";      // مستخدمو المنصّة (المالك وحده)
        public const string Settings = "platform.settings.manage"; // إعدادات المنصّة (المالك وحده)
        public const string Reports = "platform.reports.view";    // إحصاءات المنصّة
        public const string Audit = "platform.audit.view";        // سجلّ التدقيق
    }

    public static IReadOnlySet<string> StoreAll { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Catalog.Manage, Inventory.View, Inventory.Manage, Orders.View, Orders.Manage,
        Customers.View, Customers.Manage, Promotions.Manage, Reviews.Moderate,
        Store.Settings, Store.Staff, Store.Reports, Store.Payments, Store.Shipping,
    };

    public static IReadOnlySet<string> PlatformAll { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Platform.Tenants, Platform.Users, Platform.Settings, Platform.Reports, Platform.Audit,
    };

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StoreAll.Concat(PlatformAll), StringComparer.Ordinal);
}

// مصدر الحقيقة الوحيد: أيّ دور يمنح أيّ صلاحية. قدرات العميل ملكيّة (طلباته، تقييماته) تُفحص بـ
// CanAccessOwnedBy، لا صلاحيات — فالعميل لا يملك أي صلاحية إدارية.
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByRole =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Roles.PlatformOwner] = Permissions.PlatformAll,
            [Roles.PlatformAdmin] = Set(Permissions.Platform.Tenants, Permissions.Platform.Reports, Permissions.Platform.Audit),
            [Roles.TenantAdmin] = Permissions.StoreAll,
            [Roles.TenantStaff] = Set(
                Permissions.Catalog.Manage, Permissions.Inventory.View, Permissions.Inventory.Manage,
                Permissions.Orders.View, Permissions.Orders.Manage, Permissions.Customers.View,
                Permissions.Reviews.Moderate, Permissions.Store.Reports),
            [Roles.Customer] = Set(),
        };

    public static bool Grants(IEnumerable<string> roles, string permission) =>
        roles.Any(role => ByRole.TryGetValue(role, out var granted) && granted.Contains(permission));

    public static IReadOnlySet<string> For(string role) =>
        ByRole.TryGetValue(role, out var granted) ? granted : Set();

    // الأدوار التي تمنح صلاحية — لمخاطبة كل من يملكها (إشعار الإدارة بطلب جديد لمن يرى الطلبات، المرحلة 14).
    public static IReadOnlyList<string> RolesGranting(string permission) =>
        ByRole.Where(r => r.Value.Contains(permission)).Select(r => r.Key).OrderBy(r => r, StringComparer.Ordinal).ToList();

    private static IReadOnlySet<string> Set(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
