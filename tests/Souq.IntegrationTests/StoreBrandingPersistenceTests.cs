using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// إعدادات المتجر تُخزَّن مستنداً JSON بشكلٍ مستقلّ عن نموذج Domain (StoreSettingsJson). هذا
// اختيار صحيح — قاعدة تُشدَّد لاحقاً لا تُسقط متجراً حُفظ قبلها — لكنه يفتح باب عيبٍ بعينه:
//
//   حقلٌ يُضاف إلى النموذج ولا يُضاف إلى مستند التخزين، فيُقبل من الـAPI بـ204 ولا يُحفظ أبداً.
//
// وقع ذلك فعلاً في هذه الميزة: تفضيل الوضع وكشف الافتتاح قُبلا وعادا بقيمتهما الافتراضية،
// ولم يظهر الأمر إلّا عند فتح المتجر في المتصفّح. هذا الاختبار يجعل الرحلة كاملة — كتابة ثم
// قراءة عبر HTTP — هي الحكم.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class StoreBrandingPersistenceTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public StoreBrandingPersistenceTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    private static object SettingsBody(string themeMode, bool openingEnabled) => new
    {
        displayName = new Dictionary<string, string> { ["ar"] = "متجر الاختبار" },
        locale = new { defaultCulture = "ar", enabledCultures = new[] { "ar" }, timeZone = "Asia/Amman" },
        branding = new
        {
            colors = new { primary = "#1F2937", secondary = "#1F2937", accent = "#D97706", background = "#F9FAFB", text = "#111827" },
            typography = "tajawal",
            themePreset = "classic",
            themeMode,
            opening = new { enabled = openingEnabled, style = "doors" },
        },
    };

    private sealed record Opening(bool Enabled, string Style);
    private sealed record Branding(string Typography, string ThemePreset, string ThemeMode, Opening Opening, string? LogoUrl);
    private sealed record Settings(Branding Branding);
    private sealed record Config(string Name, Settings Settings);

    private async Task<Branding> BrandingOnStorefrontAsync(TestStore store) =>
        (await (await _api.ForStore(store).Anonymous().GetAsync("/api/storefront/config"))
            .Content.ReadFromJsonAsync<Config>(TestApi.Json))!.Settings.Branding;

    [Fact]
    public async Task تفضيل_الوضع_وكشف_الافتتاح_يبقيان_بعد_الحفظ()
    {
        var store = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        var response = await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/settings", SettingsBody("dark", true), TestApi.Json);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // القراءة من واجهة المتجر نفسها: هي ما يقرؤه المتصفّح فعلاً.
        var branding = await BrandingOnStorefrontAsync(store);

        branding.ThemeMode.Should().Be("dark", "الحقل قُبل بـ204 — والسؤال هل حُفظ");
        branding.Opening.Enabled.Should().BeTrue();
        branding.Opening.Style.Should().Be("doors");
    }

    [Fact]
    public async Task متجر_لم_يُضبط_له_شيء_يعود_بالافتراضي_لا_بـnull()
    {
        // متجر أُنشئ بإعدادات افتراضية (ومستنده لا يحمل الحقلين أصلاً).
        var store = await _factory.CreateStoreAsync();

        var branding = await BrandingOnStorefrontAsync(store);

        branding.ThemeMode.Should().Be("system");
        branding.Opening.Enabled.Should().BeFalse("متجر لم يطلب كشفاً لا يُعرض له");
        branding.Opening.Style.Should().Be("doors");
    }

    [Fact]
    public async Task وضع_عرض_غير_معتمد_يُرفض_لا_يُبتلع()
    {
        var store = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        var response = await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/settings", SettingsBody("sepia", false), TestApi.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await BrandingOnStorefrontAsync(store)).ThemeMode.Should().Be("system", "الرفض لا يترك حالة نصف محفوظة");
    }

    [Fact]
    public async Task تعديل_لاحق_لا_يمحو_الحقلين()
    {
        var store = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        await platform.PutAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/settings", SettingsBody("dark", true), TestApi.Json);
        await platform.PutAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/settings", SettingsBody("dark", true), TestApi.Json);

        var branding = await BrandingOnStorefrontAsync(store);
        branding.ThemeMode.Should().Be("dark");
        branding.Opening.Enabled.Should().BeTrue();
    }
}
