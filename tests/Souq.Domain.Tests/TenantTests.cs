using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Domain.Tests;

// قواعد تجمّع المتجر (Architecture.md §7): صيغة موحّدة للمعرّف والنطاق والعملة، نطاق أساسي واحد،
// انتقالات حالة محروسة، وقفل العملة بعد أي نشاط تجاري.
public class TenantTests
{
    private static Tenant NewTenant() => new("متجر تجريبي", "Demo-Store", "jod", "AR", "Asia/Amman");

    [Fact]
    public void الجديد_قيد_التجهيز_وبصيغة_موحّدة()
    {
        var tenant = NewTenant();

        tenant.Status.Should().Be(TenantStatus.Provisioning);
        tenant.Slug.Should().Be("demo-store");
        tenant.Currency.Should().Be("JOD");
        tenant.DefaultCulture.Should().Be("ar");
        tenant.Domains.Should().BeEmpty();
    }

    [Fact]
    public void تقييمات_المتجر_الجديد_تنتظر_الإشراف_حتى_يفعّل_النشر_الفوري()
    {
        // المرحلة 13 (ADR-0033): الأسلم افتراضياً؛ المتاجر القائمة أبقتها الهجرة على النشر الفوري.
        var tenant = NewTenant();
        tenant.ReviewsAutoApprove.Should().BeFalse();

        tenant.SetReviewsAutoApprove(true);

        tenant.ReviewsAutoApprove.Should().BeTrue();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("admin")]
    [InlineData("www")]
    [InlineData("under_score")]
    public void معرّف_غير_صالح_أو_محجوز_يُرفض(string slug)
    {
        var act = () => new Tenant("متجر", slug, "JOD", "ar", "Asia/Amman");

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Theory]
    [InlineData("JO")]
    [InlineData("J0D")]
    [InlineData("")]
    public void عملة_غير_صالحة_تُرفض(string currency)
    {
        var act = () => new Tenant("متجر", "store", currency, "ar", "Asia/Amman");

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Theory]
    [InlineData("fr", "Asia/Amman")]
    [InlineData("ar", "Amman")]
    [InlineData("ar", "Asia/Amman; DROP")]
    public void لغة_أو_منطقة_زمنية_غير_صالحة_تُرفض(string culture, string timeZone)
    {
        var act = () => new Tenant("متجر", "store", "JOD", culture, timeZone);

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void أول_نطاق_يصبح_الأساسي_والمضيف_بصيغة_موحّدة()
    {
        var tenant = NewTenant();

        var first = tenant.AddDomain("Shop.Example.COM.");
        var second = tenant.AddDomain("www.shop.example.com");

        first.Host.Should().Be("shop.example.com");
        first.IsPrimary.Should().BeTrue();
        second.IsPrimary.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://shop.com")]
    [InlineData("shop.com:8080")]
    [InlineData("sh op.com")]
    [InlineData("-shop.com")]
    [InlineData("")]
    public void مضيف_غير_صالح_يُرفض(string host)
    {
        var act = () => NewTenant().AddDomain(host);

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void نطاق_مكرّر_يُرفض_بأي_حالة_أحرف()
    {
        var tenant = NewTenant();
        tenant.AddDomain("shop.example.com");

        var act = () => tenant.AddDomain("SHOP.example.com");

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void تعيين_نطاق_أساسي_يُلغي_السابق_ولا_يُحذف_الأساسي_مع_وجود_غيره()
    {
        var tenant = NewTenant();
        tenant.AddDomain("one.example.com");
        tenant.AddDomain("two.example.com");

        tenant.SetPrimaryDomain("two.example.com");

        tenant.Domains.Single(d => d.IsPrimary).Host.Should().Be("two.example.com");
        var removePrimary = () => tenant.RemoveDomain("two.example.com");
        removePrimary.Should().Throw<InvalidTenantOperationException>();

        tenant.RemoveDomain("one.example.com");
        tenant.Domains.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void انتقالات_الحالة_محروسة()
    {
        var tenant = NewTenant();

        var suspendWhileProvisioning = () => tenant.Suspend();
        suspendWhileProvisioning.Should().Throw<InvalidTenantOperationException>();

        tenant.Activate();
        tenant.Suspend();
        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.Activate();
        tenant.Status.Should().Be(TenantStatus.Active);

        tenant.Archive();
        var reactivate = () => tenant.Activate();
        reactivate.Should().Throw<InvalidTenantOperationException>();
        var archiveTwice = () => tenant.Archive();
        archiveTwice.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void العملة_تُقفل_بعد_أي_نشاط_تجاري()
    {
        var tenant = NewTenant();

        tenant.ChangeCurrency("usd", hasCommercialActivity: false);
        tenant.Currency.Should().Be("USD");

        var afterActivity = () => tenant.ChangeCurrency("EUR", hasCommercialActivity: true);
        afterActivity.Should().Throw<InvalidTenantOperationException>();
        tenant.Currency.Should().Be("USD");
    }
}
