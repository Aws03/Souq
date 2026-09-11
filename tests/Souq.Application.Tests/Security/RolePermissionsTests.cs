using AwesomeAssertions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Security;
using Souq.Application.Tests.TestDoubles;
using Souq.Domain.Common;

namespace Souq.Application.Tests.Security;

// جدول الأدوار ⇒ الصلاحيات هو مصدر الحقيقة لكل قرار تفويض (API وحالات الاستخدام معاً).
public class RolePermissionsTests
{
    [Fact]
    public void المدير_يملك_كل_صلاحية_معرّفة()
    {
        string[] admin = [Roles.Admin];

        Permissions.All.Should().NotBeEmpty();
        Permissions.All.Should().OnlyContain(p => RolePermissions.Grants(admin, p));
    }

    [Fact]
    public void العميل_لا_يملك_أي_صلاحية_إدارية()
    {
        string[] customer = [Roles.Customer];

        Permissions.All.Should().NotContain(p => RolePermissions.Grants(customer, p));
        RolePermissions.For(Roles.Customer).Should().BeEmpty();
    }

    [Fact]
    public void دور_مجهول_أو_بلا_أدوار_لا_يمنح_شيئاً()
    {
        RolePermissions.Grants(["PlatformOwner-typo"], Permissions.Orders.Manage).Should().BeFalse();
        RolePermissions.Grants([], Permissions.Orders.Manage).Should().BeFalse();
    }

    [Fact]
    public void الوصول_لمورد_مملوك_للمالك_أو_لصاحب_صلاحية_الإدارة_فقط()
    {
        TestCurrentUser.Customer(5).CanAccessOwnedBy(5, Permissions.Orders.Manage).Should().BeTrue();
        TestCurrentUser.Customer(6).CanAccessOwnedBy(5, Permissions.Orders.Manage).Should().BeFalse();
        TestCurrentUser.Admin().CanAccessOwnedBy(5, Permissions.Orders.Manage).Should().BeTrue();
        TestCurrentUser.Anonymous().CanAccessOwnedBy(5, Permissions.Orders.Manage).Should().BeFalse();
    }

    [Fact]
    public void حالة_استخدام_تتطلّب_هوية_ترفض_الزائر_صراحةً()
    {
        var act = () => TestCurrentUser.Anonymous().RequireUserId();

        act.Should().Throw<AuthenticationRequiredException>();
        TestCurrentUser.Customer(8).RequireUserId().Should().Be(8);
    }
}
