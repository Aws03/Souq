using AwesomeAssertions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Security;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Common;

namespace Souq.Application.Tests.Security;

// جدول الأدوار ⇒ الصلاحيات هو مصدر الحقيقة لكل قرار تفويض (API وحالات الاستخدام معاً). عالما المنصّة
// والمتجر منفصلان: لا دور يجمع صلاحيات من الاثنين.
public class RolePermissionsTests
{
    [Fact]
    public void مالك_المنصّة_يملك_صلاحيات_المنصّة_كلها_ولا_شيء_من_المتجر()
    {
        RolePermissions.For(Roles.PlatformOwner).Should().BeEquivalentTo(Permissions.PlatformAll);
    }

    [Fact]
    public void مدير_المنصّة_يشغّل_المتاجر_بلا_مستخدمي_المنصّة_ولا_إعداداتها()
    {
        var granted = RolePermissions.For(Roles.PlatformAdmin);

        granted.Should().Contain(Permissions.Platform.Tenants).And.BeSubsetOf(Permissions.PlatformAll);
        granted.Should().NotContain([Permissions.Platform.Users, Permissions.Platform.Settings]);
    }

    [Fact]
    public void مدير_المتجر_يملك_صلاحيات_المتجر_كلها_ولا_شيء_من_المنصّة()
    {
        RolePermissions.For(Roles.TenantAdmin).Should().BeEquivalentTo(Permissions.StoreAll);
    }

    [Fact]
    public void الموظّف_يشغّل_المتجر_بلا_إعدادات_ولا_موظّفين_ولا_مال_ولا_كوبونات()
    {
        var granted = RolePermissions.For(Roles.TenantStaff);

        granted.Should().Contain([Permissions.Catalog.Manage, Permissions.Orders.Manage, Permissions.Inventory.View]);
        granted.Should().NotContain([
            Permissions.Store.Settings, Permissions.Store.Staff, Permissions.Store.Payments,
            Permissions.Promotions.Manage, Permissions.Customers.Manage,
        ]);
        granted.Should().BeSubsetOf(Permissions.StoreAll);
    }

    [Fact]
    public void العميل_والدور_المجهول_لا_يمنحان_شيئاً()
    {
        RolePermissions.For(Roles.Customer).Should().BeEmpty();
        RolePermissions.Grants(["Admin"], Permissions.Orders.Manage).Should().BeFalse("الدور القديم Admin أصبح TenantAdmin");
        RolePermissions.Grants([], Permissions.Orders.Manage).Should().BeFalse();
    }

    [Fact]
    public void كل_صلاحية_معرّفة_يمنحها_دور_واحد_على_الأقل_وعالما_المنصّة_والمتجر_لا_يتقاطعان()
    {
        Permissions.All.Should().OnlyContain(p => Roles.All.Any(role => RolePermissions.For(role).Contains(p)));
        Permissions.StoreAll.Should().NotIntersectWith(Permissions.PlatformAll);
        Roles.All.Where(Roles.IsPlatform).SelectMany(RolePermissions.For).Should().BeSubsetOf(Permissions.PlatformAll);
        Roles.All.Where(r => !Roles.IsPlatform(r)).SelectMany(RolePermissions.For).Should().BeSubsetOf(Permissions.StoreAll);
    }

    [Fact]
    public void الوصول_لمورد_مملوك_لصاحب_ملف_العميل_أو_لصاحب_صلاحية_الإدارة_فقط()
    {
        TestCurrentUser.Customer(5).CanAccessOwnedBy(5, Permissions.Orders.View).Should().BeTrue();
        TestCurrentUser.Customer(6).CanAccessOwnedBy(5, Permissions.Orders.View).Should().BeFalse();
        TestCurrentUser.Staff().CanAccessOwnedBy(5, Permissions.Orders.View).Should().BeTrue();
        TestCurrentUser.Anonymous().CanAccessOwnedBy(5, Permissions.Orders.View).Should().BeFalse();
    }

    [Fact]
    public void حالات_الشراء_تتطلّب_ملف_عميل_لا_مجرّد_هوية()
    {
        var anonymous = () => TestCurrentUser.Anonymous().RequireCustomerId();
        var staff = () => TestCurrentUser.Staff().RequireCustomerId();

        anonymous.Should().Throw<AuthenticationRequiredException>();
        staff.Should().Throw<CustomerAccountRequiredException>();
        TestCurrentUser.Customer(8).RequireCustomerId().Should().Be(8);
        TestCurrentUser.Admin(3).RequireUserId().Should().Be(3);
    }
}
