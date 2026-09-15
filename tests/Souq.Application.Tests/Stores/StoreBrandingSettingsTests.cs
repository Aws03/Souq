using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.Stores;

// ============================================================================
// إعدادات هوية المتجر الجديدة: تفضيل الوضع، وكشف الافتتاح.
//
// كلاهما يعيش في عمود JSON، ما يعني خطراً بعينه: **حقل يُضاف إلى النموذج ولا يُضاف إلى مستند
// التخزين** فيُقبل من الـAPI ولا يُحفظ أبداً — وهو ما وقع فعلاً أثناء بناء هذه الميزة، ولم
// يظهر إلّا عند تشغيل المتجر. الاختبارات هنا تُثبّت الحدّين: القبول، والبقاء بعد الحفظ.
// ============================================================================
public class StoreBrandingSettingsTests
{
    private static BrandColors Colors() =>
        BrandColors.Create("#1F2937", "#1F2937", "#D97706", "#F9FAFB", "#111827");

    // WithStyle داخلية في Domain عمداً: التعديل يمرّ بالتجمّع لا بالكائن القيمي.
    private static Tenant Store() => new("متجر", "store", "JOD", "ar", "Asia/Amman");

    [Fact]
    public void الافتراضي_نظام_الزائر_وكشف_معطّل()
    {
        // متجر لم يقرّر شيئاً: لا يفرض ضوءاً على زائر اختار وضعاً ليلياً لجهازه، ولا يعرض كشفاً.
        var branding = StoreBranding.Default;

        branding.ThemeMode.Should().Be("system");
        branding.Opening.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("system")]
    public void أوضاع_العرض_المعتمدة_تُقبل(string mode)
    {
        var tenant = Store();
        tenant.UpdateBranding(Colors(), "tajawal", "classic", mode);

        tenant.Settings.Branding.ThemeMode.Should().Be(mode);
    }

    [Fact]
    public void وضع_غير_معروف_يُرفض_بدل_أن_يُتجاهَل()
    {
        // قيمة تُبتلع بصمت تعني متجراً يظنّ أنه ضبط شيئاً ولم يفعل.
        var act = () => Store().UpdateBranding(Colors(), "tajawal", "classic", "sepia");

        act.Should().Throw<InvalidTenantOperationException>().WithMessage("*sepia*");
    }

    [Fact]
    public void نمط_افتتاح_غير_معروف_يُرفض()
    {
        var act = () => StoreOpening.Create(true, "fireworks");

        act.Should().Throw<InvalidTenantOperationException>().WithMessage("*fireworks*");
    }

    [Fact]
    public void نمط_فارغ_يأخذ_الافتراضي_لا_يرمي()
    {
        StoreOpening.Create(true, null).Style.Should().Be("doors");
        StoreOpening.Create(true, "  ").Style.Should().Be("doors");
    }

    [Fact]
    public void رفع_شعار_لا_يمحو_تفضيل_الوضع_ولا_الكشف()
    {
        // WithAsset يُعيد بناء الهوية: نسيان تمرير حقل هناك يمحوه بصمت عند أوّل رفع صورة.
        var tenant = Store();
        tenant.UpdateBranding(Colors(), "tajawal", "classic", "dark", StoreOpening.Create(true, "doors"));
        tenant.SetBrandingAsset(BrandingAsset.Logo, $"/uploads/tenants/{tenant.Id}/logo.png");

        tenant.Settings.Branding.ThemeMode.Should().Be("dark");
        tenant.Settings.Branding.Opening.Enabled.Should().BeTrue();
        tenant.Settings.Branding.LogoUrl.Should().EndWith("logo.png");
    }

    [Fact]
    public void تغيير_الألوان_وحدها_يُبقي_الوضع_والكشف()
    {
        var tenant = Store();
        tenant.UpdateBranding(Colors(), "tajawal", "classic", "dark", StoreOpening.Create(true, "doors"));
        tenant.UpdateBranding(Colors(), "cairo", "minimal");

        tenant.Settings.Branding.ThemeMode.Should().Be("dark");
        tenant.Settings.Branding.Opening.Enabled.Should().BeTrue();
        tenant.Settings.Branding.Typography.Should().Be("cairo");
    }
}
