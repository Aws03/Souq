using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.Domain.Platform;
using Souq.IntegrationTests.Infrastructure;
using static Souq.IntegrationTests.PlatformAdministrationTests;

namespace Souq.IntegrationTests;

// ============================================================================
// إدارة المتجر من داخله (المرحلة 4): الإعدادات والهوية لمتجر المضيف وحده، قواعد القراءة (WCAG) والملفات على الخادم،
// الموظّف بلا إعدادات ولا موظّفين، ودورة حياة الموظّف: دعوة على مضيف المتجر ⇒ قبول ⇒ دخول ⇒ إيقاف يُسقط جلسته.
// المتجر الافتراضي لا يُعدَّل هنا أبداً (اختبارات أخرى تعتمد مظهر ماركة) — كل اختبار بمتجر جديد.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class StoreAdministrationTests
{
    private static readonly byte[] IcoBytes = [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10, 0, 0, 0x01, 0, 0x20, 0, 0, 0];
    private static readonly byte[] SvgBytes = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public StoreAdministrationTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task مدير_المتجر_يعدّل_إعدادات_متجره_وحده_ويُدقَّق()
    {
        var a = _api.ForStore(await _factory.CreateStoreAsync());
        var b = _api.ForStore(await _factory.CreateStoreAsync());

        var update = await (await b.AdminAsync()).PutAsJsonAsync("/api/admin/store/settings", WithLocale(SettingsBody("#0B5D3B")));
        update.StatusCode.Should().Be(HttpStatusCode.NoContent, await update.Content.ReadAsStringAsync());

        (await b.Anonymous().GetFromJsonAsync<ConfigBody>("/api/storefront/config", TestApi.Json))!
            .Settings.Branding.Colors.Primary.Should().Be("#0B5D3B");
        (await a.Anonymous().GetFromJsonAsync<ConfigBody>("/api/storefront/config", TestApi.Json))!
            .Settings.Branding.Colors.Primary.Should().Be(BrandColors.Neutral.Primary);

        var owner = await _api.PlatformOwnerAsync();
        var trail = (await owner.GetFromJsonAsync<TestApi.PageBody<AuditBody>>(
            $"/api/platform/audit?tenantId={(await b.TenantAsync()).Id}&action=store.", TestApi.Json))!.Items;
        trail.Should().ContainSingle(e => e.Action == "store.settings.updated" && e.Area == "Store" && e.ActorRole == "TenantAdmin");
    }

    [Fact]
    public async Task خيارات_المحرّر_تُقرأ_من_الخادم_وما_يُعرض_منها_يُحفظ()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await store.AdminAsync();
        (await store.Anonymous().GetAsync("/api/admin/store/settings/options")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var options = (await admin.GetFromJsonAsync<OptionsBody>("/api/admin/store/settings/options", TestApi.Json))!;
        options.Cultures.Should().Equal(Tenant.SupportedCultures);
        options.Typography.Should().Equal(BrandPresets.Typography);
        options.SocialNetworks.Should().Contain(n => n.Network == "instagram" && n.Domains.Contains("instagram.com"));
        options.Limits.DisplayName.Should().Be(StoreSettings.DisplayNameMaxLength);

        // آخر خيار في كل قائمة، محفوظاً عبر HTTP ومقروءاً من إعداد الواجهة — الطريق الذي يسلكه المحرّر.
        var body = new Dictionary<string, object?>
        {
            ["displayName"] = new Dictionary<string, string> { ["ar"] = new string('س', options.Limits.DisplayName) },
            ["locale"] = new { defaultCulture = "ar", enabledCultures = options.Cultures, timeZone = "Asia/Amman" },
            ["branding"] = new
            {
                colors = new { primary = "#12355B", secondary = "#EEF2F7", accent = "#F2A541", background = "#FFFFFF", text = "#1F2933" },
                typography = options.Typography[^1], themePreset = options.ThemePresets[^1], themeMode = options.ThemeModes[^1],
                opening = new { enabled = true, style = options.OpeningStyles[^1] },
            },
        };
        var saved = await admin.PutAsJsonAsync("/api/admin/store/settings", body);
        saved.StatusCode.Should().Be(HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());

        (await store.Anonymous().GetFromJsonAsync<ConfigBody>("/api/storefront/config", TestApi.Json))!
            .Settings.Branding.Typography.Should().Be(options.Typography[^1]);
        var stored = (await admin.GetFromJsonAsync<StoredSettingsBody>("/api/admin/store/settings", TestApi.Json))!;
        stored.Branding.ThemePreset.Should().Be(options.ThemePresets[^1]);
        stored.Branding.ThemeMode.Should().Be(options.ThemeModes[^1]);
        stored.DisplayName["ar"].Should().HaveLength(options.Limits.DisplayName);
    }

    [Fact]
    public async Task لوحة_غير_مقروءة_تُرفض_ولا_يتغيّر_شيء()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());

        var response = await (await store.AdminAsync()).PutAsJsonAsync("/api/admin/store/settings",
            WithLocale(SettingsBody("#12355B", text: "#CCCCCC")));

        (await ProblemAsync(response)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidTenantOperation"));
        (await store.Anonymous().GetFromJsonAsync<ConfigBody>("/api/storefront/config", TestApi.Json))!
            .Settings.Branding.Colors.Primary.Should().Be(BrandColors.Neutral.Primary);
    }

    [Fact]
    public async Task ملفات_الهوية_بصيغها_المقبولة_فقط_وتُخدَم_على_مضيف_متجرها()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await store.AdminAsync();

        var favicon = await admin.PostAsync("/api/admin/store/branding/favicon", FileBody(IcoBytes, "favicon.ico"));
        favicon.StatusCode.Should().Be(HttpStatusCode.OK, await favicon.Content.ReadAsStringAsync());
        var url = (await favicon.Content.ReadFromJsonAsync<UrlBody>(TestApi.Json))!.Url;
        url.Should().EndWith(".ico");
        var served = await store.Anonymous().GetAsync(url);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/x-icon");
        (await _api.Anonymous().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);   // مضيف متجر آخر

        (await ProblemAsync(await admin.PostAsync("/api/admin/store/branding/logo", FileBody(SvgBytes, "logo.svg"))))
            .Should().Be((HttpStatusCode.BadRequest, "UnsupportedMediaType"));
        (await ProblemAsync(await admin.PostAsync("/api/admin/store/branding/logo", FileBody(IcoBytes, "logo.ico"))))
            .Should().Be((HttpStatusCode.BadRequest, "UnsupportedMediaType"));
    }

    [Fact]
    public async Task الموظّف_يدير_التشغيل_لا_الإعدادات_ولا_الموظّفين()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var staff = await storeApi.LoginAsync(
            await _factory.CreateStoreUserAsync(store.Tenant, Roles.TenantStaff), SouqApiFactory.StoreAdminPassword);

        (await staff.GetAsync("/api/admin/store/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.GetAsync("/api/admin/store/settings/options")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.GetAsync("/api/admin/staff")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task دعوة_موظّف_وقبولها_ودخوله_ثم_إيقافه_يُسقط_جلسته_فوراً()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var admin = await storeApi.AdminAsync();
        var email = $"clerk-{Guid.NewGuid():N}@store.test";

        (await admin.PostAsJsonAsync("/api/admin/staff", new { fullName = "Clerk", email, role = "TenantStaff" })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        await _factory.DispatchNotificationsAsync();   // المرحلة 14: الدعوة من صندوق الصادر
        new Uri(_factory.Emails.LastInvitationLinkFor(email)).Host.Should().Be(store.Host);
        var firstToken = _factory.Emails.LastInvitationTokenFor(email);

        // إعادة الإرسال تجدّد الرمز: رابط الرسالة الأولى لم يعد صالحاً.
        var resend = await admin.PostAsJsonAsync("/api/admin/staff", new { fullName = "Clerk", email, role = "TenantStaff" });
        (await resend.Content.ReadFromJsonAsync<InvitationBody>(TestApi.Json))!.Renewed.Should().BeTrue();
        await _factory.DispatchNotificationsAsync();
        _factory.Emails.LastInvitationTokenFor(email).Should().NotBe(firstToken);
        (await ProblemAsync(await storeApi.Anonymous().PostAsJsonAsync("/api/auth/reset-password",
                new { token = firstToken, newPassword = "Clerk-Pass-1" })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidResetToken"));

        await _api.AcceptInvitationAsync(_factory.Emails.LastInvitationLinkFor(email), "Clerk-Pass-1");
        var clerk = await storeApi.LoginAsync(email, "Clerk-Pass-1");
        (await clerk.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK);

        var staffList = (await admin.GetFromJsonAsync<TestApi.PageBody<AccountBody>>("/api/admin/staff?pageSize=100", TestApi.Json))!;
        var clerkAccount = staffList.Items.Single(a => a.Email == email);
        clerkAccount.InvitationPending.Should().BeFalse();

        var adminId = (await admin.GetFromJsonAsync<TestApi.UserBody>("/api/auth/me", TestApi.Json))!.Id;
        (await ProblemAsync(await admin.PostAsJsonAsync($"/api/admin/staff/{adminId}/status", new { active = false })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "CannotDisableSelf"));

        (await admin.PostAsJsonAsync($"/api/admin/staff/{clerkAccount.Id}/status", new { active = false })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await clerk.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemAsync(await storeApi.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = "Clerk-Pass-1" })))
            .Should().Be((HttpStatusCode.Unauthorized, "AccountDisabled"));
    }

    [Fact]
    public async Task بريد_عميل_قائم_لا_يُدعى_موظّفاً()
    {
        var storeApi = _api.ForStore(await _factory.CreateStoreAsync());
        var (_, email) = await storeApi.NewCustomerAsync();

        (await ProblemAsync(await (await storeApi.AdminAsync()).PostAsJsonAsync("/api/admin/staff",
                new { fullName = "عميل", email, role = "TenantStaff" })))
            .Should().Be((HttpStatusCode.Conflict, "EmailTaken"));
    }

    // الإعدادات من منظور متجر اختبار: لغته الافتراضية ar (CreateStoreAsync) ⇒ تبقى ضمن المفعّلة.
    private static object WithLocale(object settings) => new Dictionary<string, object?>(
        settings.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(settings)))
    {
        ["locale"] = new { defaultCulture = "ar", enabledCultures = new[] { "ar", "en" }, timeZone = "Asia/Amman" },
    };

    private sealed record InvitationBody(int UserId, bool Renewed);
    private sealed record OptionsBody(
        List<string> Cultures, List<string> Typography, List<string> ThemePresets, List<string> ThemeModes,
        List<string> OpeningStyles, List<NetworkBody> SocialNetworks, LimitsBody Limits);
    private sealed record StoredSettingsBody(Dictionary<string, string> DisplayName, StoredBrandingBody Branding);
    private sealed record StoredBrandingBody(string ThemePreset, string ThemeMode);
    private sealed record NetworkBody(string Network, List<string> Domains);
    private sealed record LimitsBody(int DisplayName, int Announcement, int SeoTitle, int SeoDescription);
}
