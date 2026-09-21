using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Domain.Tests;

// التجمّع يحرس إعداداته (المرحلة 4): افتراضي صالح لمتجر جديد، لغة افتراضية مفعّلة دائماً، خطوط وقوالب معتمدة،
// ملفات هوية تحت بادئته فقط، وحدات معروفة، وروابط تواصل محدودة.
public class TenantSettingsTests
{
    private static Tenant NewTenant() => new("متجر تجريبي", "demo-store", "JOD", "ar", "Asia/Amman");

    [Fact]
    public void المتجر_الجديد_بإعدادات_افتراضية_وكل_الوحدات()
    {
        var tenant = NewTenant();

        tenant.HasCustomSettings.Should().BeTrue();
        tenant.Settings.DisplayName.Should().Equal(new Dictionary<string, string> { ["ar"] = "متجر تجريبي" });
        tenant.Settings.EnabledCultures.Should().Equal("ar");
        tenant.Settings.Branding.Colors.Should().BeSameAs(BrandColors.Neutral);
        tenant.Modules.Should().BeEquivalentTo(StoreModules.All);
    }

    [Fact]
    public void اللغة_الافتراضية_مفعّلة_دائماً()
    {
        var tenant = NewTenant();

        ((Action)(() => tenant.SetEnabledCultures(["en"]))).Should().Throw<InvalidTenantOperationException>();
        ((Action)(() => tenant.SetEnabledCultures(["ar", "fr"]))).Should().Throw<InvalidTenantOperationException>();

        tenant.SetEnabledCultures(["EN", "ar"]);
        tenant.Settings.EnabledCultures.Should().Equal("ar", "en");     // بترتيب اللغات المدعومة

        var arabicOnly = NewTenant();
        arabicOnly.SetLocale("en", "UTC");
        arabicOnly.Settings.EnabledCultures.Should().Contain("en");
    }

    [Fact]
    public void الخط_والقالب_من_القائمة_المعتمدة_فقط()
    {
        var tenant = NewTenant();

        ((Action)(() => tenant.UpdateBranding(BrandColors.Neutral, "comic-sans", "classic"))).Should().Throw<InvalidTenantOperationException>();
        ((Action)(() => tenant.UpdateBranding(BrandColors.Neutral, "cairo", "neon"))).Should().Throw<InvalidTenantOperationException>();

        tenant.UpdateBranding(BrandColors.Neutral, "Cairo", "Minimal");
        tenant.Settings.Branding.Typography.Should().Be("cairo");
        tenant.Settings.Branding.ThemePreset.Should().Be("minimal");
    }

    [Theory]
    [InlineData("https://cdn.evil.example/logo.png")]
    [InlineData("/uploads/tenants/99/branding/logo.png")]
    [InlineData("/uploads/tenants/0/../99/branding/logo.png")]
    [InlineData("")]
    public void ملف_هوية_خارج_بادئة_المتجر_يُرفض(string url)
    {
        var act = () => NewTenant().SetBrandingAsset(BrandingAsset.Logo, url);

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void ملف_الهوية_يُحفظ_ولا_يمسّه_تعديل_الألوان()
    {
        var tenant = NewTenant();

        tenant.SetBrandingAsset(BrandingAsset.Favicon, "/uploads/tenants/0/branding/icon.png");
        tenant.UpdateBranding(BrandColors.Neutral, "almarai", "bold");

        tenant.Settings.Branding.FaviconUrl.Should().Be("/uploads/tenants/0/branding/icon.png");
        tenant.Settings.Branding.LogoUrl.Should().BeNull();
    }

    [Fact]
    public void الوحدات_تستبدل_كاملة_والمجهول_يُرفض()
    {
        var tenant = NewTenant();

        tenant.SetModules(["reviews"]);
        tenant.Modules.Should().BeEquivalentTo(["reviews"]);
        tenant.SetModules([]);
        tenant.Modules.Should().BeEmpty();
        ((Action)(() => tenant.SetModules(["marketplace"]))).Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void روابط_التواصل_محدودة_ورابط_واحد_لكل_شبكة()
    {
        var tenant = NewTenant();
        var instagram = SocialLink.Create("instagram", "https://instagram.com/a");

        var duplicate = () => tenant.UpdateStorefront(null, StoreContact.Empty, [instagram, instagram], SeoSettings.Empty, null);

        duplicate.Should().Throw<InvalidTenantOperationException>();
    }

    // TD-42 (قرار C): روابط السياسات — النوع من قائمة مغلقة، والرابط https مطلق وحده، والفارغ حذف.
    [Fact]
    public void روابط_السياسات_أنواعها_مغلقة_وروابطها_https_وحدها()
    {
        var tenant = NewTenant();

        var links = StorePolicyLinks.Create(new Dictionary<string, string?>
        {
            ["privacy"] = " https://example.test/privacy ",
            ["terms"] = "",                       // فارغ ⇒ يُحذف، لا رابط بلا هدف
        });
        tenant.UpdateStorefront(null, StoreContact.Empty, [], SeoSettings.Empty, null, links);

        tenant.Settings.Policies.UrlFor("privacy").Should().Be("https://example.test/privacy");
        tenant.Settings.Policies.UrlFor("terms").Should().BeNull();
        tenant.Settings.Policies.Urls.Should().HaveCount(1);

        // نوع مجهول لا يُتجاهَل: تاجرٌ يظنّ أنه ضبط شيئاً ولم يفعل أسوأ من خطأ صريح.
        ((Action)(() => StorePolicyLinks.Create(new Dictionary<string, string?> { ["cookies"] = "https://a.test/c" })))
            .Should().Throw<InvalidTenantOperationException>();

        foreach (var bad in new[] { "http://example.test/p", "javascript:alert(1)", "/pages/privacy", "example.test/p" })
        {
            ((Action)(() => StorePolicyLinks.Create(new Dictionary<string, string?> { ["privacy"] = bad })))
                .Should().Throw<InvalidTenantOperationException>($"رابط سياسة غير https مطلق يُرفض: {bad}");
        }

        ((Action)(() => StorePolicyLinks.Create(new Dictionary<string, string?>
            { ["faq"] = "https://example.test/" + new string('a', StorePolicyLinks.UrlMaxLength) })))
            .Should().Throw<InvalidTenantOperationException>();
    }

    // المُنادي الذي لا يعرف الروابط لا يمسحها، ومحرّر الإعدادات يمرّرها دائماً فيقدر على مسحها.
    [Fact]
    public void تحديث_الواجهة_بلا_سياسات_يبقيها_وبفارغة_يمسحها()
    {
        var tenant = NewTenant();
        var links = StorePolicyLinks.Create(new Dictionary<string, string?> { ["privacy"] = "https://example.test/p" });
        tenant.UpdateStorefront(null, StoreContact.Empty, [], SeoSettings.Empty, null, links);

        tenant.UpdateStorefront(null, StoreContact.Empty, [], SeoSettings.Empty, null);
        tenant.Settings.Policies.Urls.Should().HaveCount(1, "مُنادٍ لا يذكر الروابط لا يقصد مسحها");

        tenant.UpdateStorefront(null, StoreContact.Empty, [], SeoSettings.Empty, null, StorePolicyLinks.Empty);
        tenant.Settings.Policies.Urls.Should().BeEmpty("روابط فارغة صريحة مسحٌ مقصود");
    }

    [Fact]
    public void اسم_المتجر_بلا_محارف_تحكّم()
    {
        var act = () => NewTenant().Rename("متجر\r\nBcc: victim@example.com");

        act.Should().Throw<InvalidTenantOperationException>();
    }
}
