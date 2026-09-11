using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Domain.Tests;

// إعدادات الواجهة (WhiteLabel.md §2): ألوان مقروءة بحساب WCAG الفعلي، روابط تواصل على نطاق شبكتها، نصوص لكل لغة
// مدعومة بحدود طول، وخطوط وقوالب من القائمة المعتمدة.
public class StoreSettingsTests
{
    [Fact]
    public void الهوية_المحايدة_ولوحة_ماركة_تجتازان_قواعد_التباين()
    {
        var neutral = BrandColors.Neutral;
        var recreate = () => BrandColors.Create(neutral.Primary, neutral.Secondary, neutral.Accent, neutral.Background, neutral.Text);
        recreate.Should().NotThrow();

        var marka = BrandColors.Create("#0F3B3A", "#F0EBE1", "#E8A33D", "#FAF7F1", "#1A2421");
        marka.OnPrimary.Should().Be("#FFFFFF");     // نص أبيض على البترولي
        marka.OnAccent.Should().Be("#1A2421");      // نص داكن على الزعفراني
    }

    [Fact]
    public void نسبة_التباين_تطابق_تعريف_WCAG()
    {
        BrandColors.ContrastRatio("#FFFFFF", "#000000").Should().BeApproximately(21, 0.01);
        BrandColors.ContrastRatio("#777777", "#FFFFFF").Should().BeApproximately(4.48, 0.01);
        BrandColors.ContrastRatio("#FFFFFF", "#777777").Should().BeApproximately(4.48, 0.01);
    }

    [Theory]
    [InlineData("#1E3A5F", "#E9EEF3", "#F2A541", "#FFFFFF", "#AAAAAA")]   // نص فاتح على أبيض (2.3:1)
    [InlineData("#808080", "#E9EEF3", "#F2A541", "#FFFFFF", "#1F2933")]   // رمادي: لا الأبيض ولا الداكن مقروء عليه
    [InlineData("#1E3A5F", "#203040", "#1E3A5F", "#102030", "#FFFFFF")]   // الأساسي يذوب في الخلفية (1.4:1)
    [InlineData("red", "#E9EEF3", "#F2A541", "#FFFFFF", "#1F2933")]       // ليس hex
    [InlineData("#12345", "#E9EEF3", "#F2A541", "#FFFFFF", "#1F2933")]
    public void لوحة_غير_مقروءة_أو_بصيغة_خاطئة_تُرفض(string primary, string secondary, string accent, string background, string text)
    {
        var act = () => BrandColors.Create(primary, secondary, accent, background, text);

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Theory]
    [InlineData("instagram", "https://www.instagram.com/marka")]
    [InlineData("whatsapp", "https://wa.me/962700000000")]
    [InlineData("X", "https://x.com/marka")]
    public void رابط_تواصل_على_نطاق_شبكته_يُقبل(string network, string url)
    {
        var link = SocialLink.Create(network, url);

        link.Network.Should().Be(network.ToLowerInvariant());
        link.Url.Should().StartWith("https://");
    }

    [Theory]
    [InlineData("instagram", "http://instagram.com/marka")]            // ليس https
    [InlineData("instagram", "https://evil.example/instagram.com")]    // نطاق آخر
    [InlineData("instagram", "https://instagram.com.evil.example/")]   // نطاق متنكّر
    [InlineData("instagram", "javascript:alert(1)")]
    [InlineData("myspace", "https://myspace.com/marka")]               // شبكة غير مدعومة
    public void رابط_تواصل_مريب_يُرفض(string network, string url)
    {
        var act = () => SocialLink.Create(network, url);

        act.Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void التواصل_يُطبَّع_والقيم_الخاطئة_تُرفض()
    {
        var contact = StoreContact.Create(" Support@Store.Example ", "+962 6 000 0000",
            new Dictionary<string, string?> { ["ar"] = " عمّان ", ["en"] = "  " });

        contact.Email.Should().Be("support@store.example");
        contact.Address.Should().Equal(new Dictionary<string, string> { ["ar"] = "عمّان" });

        ((Action)(() => StoreContact.Create("not-an-email", null, null))).Should().Throw<InvalidTenantOperationException>();
        ((Action)(() => StoreContact.Create(null, "call me", null))).Should().Throw<InvalidTenantOperationException>();
        ((Action)(() => StoreContact.Create(null, null, new Dictionary<string, string?> { ["fr"] = "Paris" })))
            .Should().Throw<InvalidTenantOperationException>();
    }

    [Fact]
    public void نصوص_SEO_بحدودها()
    {
        var tooLong = new string('x', SeoSettings.TitleMaxLength + 1);

        var act = () => SeoSettings.Create(new Dictionary<string, string?> { ["ar"] = tooLong }, null);

        act.Should().Throw<InvalidTenantOperationException>();
        SeoSettings.Create(new Dictionary<string, string?> { ["EN"] = "Marka" }, null).Title
            .Should().Equal(new Dictionary<string, string> { ["en"] = "Marka" });
    }

    [Fact]
    public void الوحدات_تُطبَّع_والمجهول_يُرفض_والقراءة_متسامحة()
    {
        StoreModules.Format(["Reviews", "promotions", "reviews"]).Should().Be("promotions,reviews");
        StoreModules.Format([]).Should().BeEmpty();
        ((Action)(() => StoreModules.Format(["loyalty"]))).Should().Throw<InvalidTenantOperationException>();

        StoreModules.Parse("reviews,retired-module,,wishlist").Should().BeEquivalentTo(["reviews", "wishlist"]);
        StoreModules.Parse(null).Should().BeEmpty();
    }
}
