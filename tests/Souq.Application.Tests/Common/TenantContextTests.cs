using AwesomeAssertions;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Tenancy;
using Souq.Application.Tests.TestDoubles;

namespace Souq.Application.Tests.Common;

// سياق المستأجر يُضبط مرة واحدة لكل نطاق خدمات، وغيابه خطأ صاخب لا "كل المتاجر".
public class TenantContextTests
{
    [Fact]
    public void يُضبط_مرة_واحدة_ولا_يُبدَّل_في_منتصف_النطاق()
    {
        var context = new TenantContext();
        context.UseTenant(TestTenant.Info(id: 1));

        var switchTenant = () => context.UseTenant(TestTenant.Info(id: 2));
        var switchToPlatform = () => context.UsePlatform();

        switchTenant.Should().Throw<InvalidOperationException>();
        switchToPlatform.Should().Throw<InvalidOperationException>();
        context.Tenant!.Id.Should().Be(1);
    }

    [Fact]
    public void RequireTenant_يرمي_بلا_متجر_وفي_نطاق_المنصّة()
    {
        var none = new TenantContext();
        var platform = new TenantContext();
        platform.UsePlatform();

        ((Action)(() => none.RequireTenant())).Should().Throw<TenantContextMissingException>();
        ((Action)(() => platform.RequireTenant())).Should().Throw<TenantContextMissingException>();
        platform.Scope.Should().Be(TenantScope.Platform);
    }
}
