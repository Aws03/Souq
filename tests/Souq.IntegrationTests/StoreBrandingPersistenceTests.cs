using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.Domain.Platform;
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

    private static object SettingsBody(string themeMode, bool openingEnabled, object[]? sections = null) => new
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
        sections,
    };

    private sealed record Opening(bool Enabled, string Style);
    private sealed record Branding(string Typography, string ThemePreset, string ThemeMode, Opening Opening, string? LogoUrl);
    private sealed record Section(string Type, bool Enabled);
    private sealed record Settings(
        Branding Branding, IReadOnlyList<Section> Sections, IReadOnlyList<string> EnabledSections);
    private sealed record Config(string Name, Settings Settings);

    private async Task<Settings> StorefrontSettingsAsync(TestStore store) =>
        (await (await _api.ForStore(store).Anonymous().GetAsync("/api/storefront/config"))
            .Content.ReadFromJsonAsync<Config>(TestApi.Json))!.Settings;

    private async Task<Branding> BrandingOnStorefrontAsync(TestStore store) =>
        (await StorefrontSettingsAsync(store)).Branding;

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

// ============================================================================
// أقسام الرئيسية عبر الرحلة كاملةً (C8، ADR-0060): تُحفظ من المنصّة، وتُقرأ من **واجهة المتجر**
// — وهي ما يقرؤه المتصفّح فعلاً. والمستندُ نفسه هو الفخّ الذي يوجد هذا الملفّ لأجله: حقلٌ
// يُقبل بـ204 ولا يُحفظ لا يظهر إلّا حين يفتح أحدٌ المتجر.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class StoreSectionsPersistenceTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public StoreSectionsPersistenceTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    private static object Body(object[]? sections) => new
    {
        displayName = new Dictionary<string, string> { ["ar"] = "متجر الاختبار" },
        locale = new { defaultCulture = "ar", enabledCultures = new[] { "ar" }, timeZone = "Asia/Amman" },
        branding = new
        {
            colors = new { primary = "#1F2937", secondary = "#1F2937", accent = "#D97706", background = "#F9FAFB", text = "#111827" },
            typography = "tajawal",
            themePreset = "classic",
        },
        sections,
    };

    private sealed record Section(string Type, bool Enabled);
    private sealed record Settings(IReadOnlyList<Section> Sections, IReadOnlyList<string> EnabledSections);
    private sealed record Config(Settings Settings);

    private async Task<Settings> OnStorefrontAsync(TestStore store) =>
        (await (await _api.ForStore(store).Anonymous().GetAsync("/api/storefront/config"))
            .Content.ReadFromJsonAsync<Config>(TestApi.Json))!.Settings;

    [Fact]
    public async Task متجر_لم_يضبط_أقسامه_يعود_بالترتيب_الافتراضي_كاملاً()
    {
        var store = await _factory.CreateStoreAsync();

        var settings = await OnStorefrontAsync(store);

        settings.EnabledSections.Should().Equal(StoreSections.Types,
            "مستندٌ بلا هذا الحقل يجب أن يقرأ الرئيسية كما هي اليوم — الترقية بلا أثر");
    }

    [Fact]
    public async Task الترتيب_والإطفاء_يبقيان_بعد_الحفظ()
    {
        var store = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        object[] chosen =
        [
            new { type = "catalog", enabled = true },
            new { type = "hero", enabled = true },
            new { type = "offers", enabled = false },
        ];

        var response = await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/settings", Body(chosen), TestApi.Json);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var settings = await OnStorefrontAsync(store);

        settings.EnabledSections.Should().Equal(["catalog", "hero"],
            "الترتيب كما أُرسل، والمُطفأ غائب — وما لم يُذكر يُلحق مُطفأً");
        settings.Sections.Should().Contain(s => s.Type == "offers" && !s.Enabled);
        settings.Sections.Select(s => s.Type).Should().BeEquivalentTo(StoreSections.Types,
            "القائمة الكاملة تصل المحرّر كي يعرف ما يمكن تشغيله");
    }

    // الحقلُ غائبٌ من عميلٍ أقدم ⇒ التخطيط كما هو. وهذا ليس تفصيلاً: بقيّةُ هذا العقد تستبدل
    // ما فيه، فلو أخذ هذا الحقلُ القاعدةَ نفسها لمحا كلُّ حفظٍ من شاشةٍ قديمة تخطيطَ التاجر.
    [Fact]
    public async Task حفظٌ_بلا_ذكر_الأقسام_لا_يمحو_التخطيط()
    {
        var store = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        object[] chosen = [new { type = "catalog", enabled = true }, new { type = "hero", enabled = true }];
        await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/settings", Body(chosen), TestApi.Json);

        var second = await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/settings", Body(null), TestApi.Json);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await OnStorefrontAsync(store)).EnabledSections.Should().Equal(["catalog", "hero"]);
    }

    // العزل: تخطيطُ متجرٍ لا يصل متجراً آخر. الإعداد مستندٌ على صفّ المتجر، فالعزلُ بنيويّ —
    // وهذا يثبته على قاعدةٍ حقيقية بدل الاكتفاء بأنّه «يجب أن يكون كذلك».
    [Fact]
    public async Task تخطيط_متجر_لا_يمسّ_متجراً_آخر()
    {
        var first = await _factory.CreateStoreAsync();
        var second = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        var response = await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{first.Tenant.Id}/settings",
            Body([new { type = "catalog", enabled = true }, new { type = "hero", enabled = true }]), TestApi.Json);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await OnStorefrontAsync(first)).EnabledSections.Should().Equal(["catalog", "hero"]);
        (await OnStorefrontAsync(second)).EnabledSections.Should().Equal(StoreSections.Types,
            "المتجر الثاني لم يُضبَط، فيبقى على الافتراضي");
    }

    [Fact]
    public async Task نوع_قسم_مجهول_يُرفض_لا_يُبتلع()
    {
        var store = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        var response = await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/settings",
            Body([new { type = "carousel", enabled = true }]), TestApi.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task إطفاء_الكتالوج_يُرفض()
    {
        var store = await _factory.CreateStoreAsync();
        var platform = await _api.PlatformOwnerAsync();

        var response = await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{store.Tenant.Id}/settings",
            Body([new { type = "catalog", enabled = false }]), TestApi.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
