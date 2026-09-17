using AwesomeAssertions;
using Souq.Application.Features.Stores;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Application.Tests.Stores;

// ============================================================================
// خيارات محرّر الإعدادات: ما تعرضه النقطة يجب أن يكون ما يقبله Domain فعلاً — لا أكثر ولا أقل.
// الاختبار لا يقارن القوائم بنفسها (ذلك يمرّ دائماً)، بل يمرّر كل خيار معروض إلى قواعد Domain:
// خيار يُعرض ويُرفض يعني تاجراً يختار شيئاً من القائمة فيتلقّى خطأً، وحدٌّ يُعرض أوسع من الحقيقي
// يعني عدّاداً في الواجهة يقول "مقبول" لنصٍّ سيُرفض.
// ============================================================================
public class StoreSettingsOptionsTests
{
    private static readonly StoreSettingsOptionsDto Options =
        new GetStoreSettingsOptionsHandler().Handle(new GetStoreSettingsOptionsQuery(), CancellationToken.None).Result;

    private static BrandColors Colors() => BrandColors.Neutral;
    private static Tenant Store() => new("متجر", "store", "JOD", "ar", "Asia/Amman");

    [Fact]
    public void كل_خطّ_وقالب_ووضع_معروض_يقبله_المتجر()
    {
        foreach (var typography in Options.Typography)
        foreach (var theme in Options.ThemePresets)
        foreach (var mode in Options.ThemeModes)
        {
            var tenant = Store();
            tenant.UpdateBranding(Colors(), typography, theme, mode);
            tenant.Settings.Branding.Typography.Should().Be(typography);
        }

        Options.OpeningStyles.Should().AllSatisfy(style => StoreOpening.Create(true, style).Style.Should().Be(style));
    }

    [Fact]
    public void كل_لغة_معروضة_تُفعَّل_وتكون_افتراضية()
    {
        var tenant = Store();
        tenant.SetEnabledCultures(Options.Cultures);
        foreach (var culture in Options.Cultures) tenant.SetLocale(culture, "UTC");

        tenant.Settings.EnabledCultures.Should().BeEquivalentTo(Options.Cultures);
    }

    [Fact]
    public void كل_شبكة_تقبل_رابطاً_على_كل_نطاق_معروض_لها_وترفض_غيره()
    {
        Options.SocialNetworks.Should().NotBeEmpty();
        foreach (var network in Options.SocialNetworks)
        {
            network.Domains.Should().AllSatisfy(domain =>
                SocialLink.Create(network.Network, $"https://{domain}/store").Network.Should().Be(network.Network));

            var elsewhere = () => SocialLink.Create(network.Network, "https://example.com/store");
            elsewhere.Should().Throw<InvalidTenantOperationException>();
        }
    }

    [Theory]
    [InlineData("displayName")]
    [InlineData("announcement")]
    [InlineData("seoTitle")]
    [InlineData("seoDescription")]
    [InlineData("address")]
    public void الحدّ_المعروض_هو_الحدّ_الحقيقي_لا_أوسع_ولا_أضيق(string field)
    {
        var limit = field switch
        {
            "displayName" => Options.Limits.DisplayName,
            "announcement" => Options.Limits.Announcement,
            "seoTitle" => Options.Limits.SeoTitle,
            "seoDescription" => Options.Limits.SeoDescription,
            _ => Options.Limits.Address,
        };

        Apply(field, new string('a', limit)).Should().NotThrow();
        Apply(field, new string('a', limit + 1)).Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void عتبات_التباين_هي_التي_ترفض_بها_اللوحة()
    {
        Options.Contrast.Text.Should().Be(BrandColors.MinimumTextContrast);
        Options.Contrast.Ui.Should().Be(BrandColors.MinimumUiContrast);
        Options.Limits.SocialLinks.Should().Be(StoreSettings.MaxSocialLinks);
        Options.Limits.BrandingFileBytes.Should().Be(BrandingFiles.MaxBytes);
    }

    private static Action Apply(string field, string value)
    {
        var text = new Dictionary<string, string?> { ["ar"] = value };
        return field switch
        {
            "displayName" => () => Store().UpdateStorefront(text, StoreContact.Empty, [], SeoSettings.Empty, null),
            "announcement" => () => Store().UpdateStorefront(null, StoreContact.Empty, [], SeoSettings.Empty, text),
            "seoTitle" => () => SeoSettings.Create(text, null),
            "seoDescription" => () => SeoSettings.Create(null, text),
            _ => () => StoreContact.Create(null, null, text),
        };
    }
}
