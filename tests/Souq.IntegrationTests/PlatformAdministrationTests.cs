using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Auditing;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// منطقة المنصّة عبر HTTP الحقيقي (المرحلة 4 — معيار الخروج): إنشاء متجر ⇒ نطاق وهوية ⇒ وحدات ⇒ دعوة مديره ⇒ دخوله
// على نطاق متجره؛ المتجر الموقوف تعلن واجهته أنه غير متاح؛ وكل خطوة في سجلّ التدقيق بفاعلها ومتجرها. ثم حدود
// المنصّة: نطاق مسروق مرفوض، العملة تُقفل بعد النشاط، حسابات المنصّة للمالك وحده، والسجلّ للإضافة فقط.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class PlatformAdministrationTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public PlatformAdministrationTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task تجهيز_متجر_كامل_من_المنصّة_حتى_دخول_مديره_على_نطاقه_ثم_إيقافه()
    {
        var owner = await _api.PlatformOwnerAsync();
        var slug = $"acme{Guid.NewGuid():N}"[..16];
        var host = $"{slug}.shop.test";

        // (1) المتجر يبدأ قيد التجهيز.
        var created = await owner.PostAsJsonAsync("/api/platform/tenants",
            new { name = "Acme", slug, currency = "USD", defaultCulture = "en", timeZone = "UTC" });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var tenantId = (await created.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;

        // (2) النطاق والهوية والوحدات وملف الشعار.
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/domains", new { host })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var settings = await owner.PutAsJsonAsync($"/api/platform/tenants/{tenantId}/settings", SettingsBody("#12355B"));
        settings.StatusCode.Should().Be(HttpStatusCode.NoContent, await settings.Content.ReadAsStringAsync());
        (await owner.PutAsJsonAsync($"/api/platform/tenants/{tenantId}/modules", new { modules = new[] { "promotions", "wishlist" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var logo = await owner.PostAsync($"/api/platform/tenants/{tenantId}/branding/Logo", FileBody(PngBytes, "logo.png"));
        logo.StatusCode.Should().Be(HttpStatusCode.OK, await logo.Content.ReadAsStringAsync());
        var logoUrl = (await logo.Content.ReadFromJsonAsync<UrlBody>(TestApi.Json))!.Url;
        logoUrl.Should().StartWith($"/uploads/tenants/{tenantId}/branding/");

        // (3) دعوة المدير: الرابط على نطاق المتجر لا على مضيف المنصّة.
        var email = $"boss-{Guid.NewGuid():N}@acme.test";
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/admins", new { fullName = "Acme Boss", email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.DispatchNotificationsAsync();   // المرحلة 14: الدعوة من صندوق الصادر (داخل نطاق المتجر)
        var link = new Uri(_factory.Emails.LastInvitationLinkFor(email));
        link.Host.Should().Be(host);
        link.AbsolutePath.Should().Be("/accept-invitation");

        // (4) القبول والدخول على مضيف المتجر (والمتجر قيد التجهيز)، وتوكنه لا يصلح على مضيف المنصّة.
        await _api.AcceptInvitationAsync(link.ToString(), "Acme-Boss-Pass-1");
        var bossToken = await _api.TokenOnAsync(host, email, "Acme-Boss-Pass-1");
        (await _api.Authorized(bossToken, host).GetAsync("/api/admin/store/settings")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.Authorized(bossToken, SouqApiFactory.PlatformHost).GetAsync("/api/platform/tenants")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        // (5) الواجهة العامة مغلقة حتى التفعيل، ثم تعكس ما ضبطته المنصّة.
        (await _api.Client(host).GetAsync("/api/storefront/config")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/status", new { action = "Activate" })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var config = (await _api.Client(host).GetFromJsonAsync<ConfigBody>("/api/storefront/config", TestApi.Json))!;
        config.Slug.Should().Be(slug);
        config.Modules.Should().BeEquivalentTo(["promotions", "wishlist"]);
        config.Settings.Branding.Colors.Primary.Should().Be("#12355B");
        config.Settings.Branding.Colors.OnPrimary.Should().Be("#FFFFFF");
        config.Settings.Branding.LogoUrl.Should().Be(logoUrl);
        config.Settings.Locale.Currency.Should().Be("USD");
        config.Settings.Locale.CurrencyDecimals.Should().Be(2);
        config.Settings.Locale.EnabledCultures.Should().Equal("ar", "en");
        (await _api.Client(host).GetAsync(logoUrl)).StatusCode.Should().Be(HttpStatusCode.OK);

        // (6) الوحدة المعطّلة مغلقة على الخادم.
        (await ProblemAsync(await _api.Client(host).GetAsync("/api/products/1/reviews")))
            .Should().Be((HttpStatusCode.NotFound, "ModuleDisabled"));

        // (7) الإيقاف يغلق الواجهة فوراً على هذه النسخة.
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/status", new { action = "Suspend" })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await ProblemAsync(await _api.Client(host).GetAsync("/api/storefront/config")))
            .Should().Be((HttpStatusCode.ServiceUnavailable, "StoreUnavailable"));

        // (8) كل خطوة في سجلّ التدقيق بفاعلها (المالك) ومنطقتها ومتجرها.
        var ownerId = (await owner.GetFromJsonAsync<TestApi.UserBody>("/api/auth/me", TestApi.Json))!.Id;
        var trail = (await owner.GetFromJsonAsync<TestApi.PageBody<AuditBody>>(
            $"/api/platform/audit?tenantId={tenantId}&pageSize=100", TestApi.Json))!.Items;
        trail.Select(a => a.Action).Should().Contain([
            "tenant.domain.added", "tenant.settings.updated", "tenant.modules.updated", "tenant.branding.uploaded",
            "tenant.admin.invited", "tenant.activated", "tenant.suspended",
        ]);
        trail.Where(a => a.Action.StartsWith("tenant.", StringComparison.Ordinal))
            .Should().OnlyContain(a => a.ActorUserId == ownerId && a.Area == "Platform" && a.ActorRole == "PlatformOwner");
        var creation = (await owner.GetFromJsonAsync<TestApi.PageBody<AuditBody>>(
            "/api/platform/audit?action=tenant.created&pageSize=100", TestApi.Json))!.Items;
        creation.Should().Contain(a => a.TargetId == slug);
    }

    [Fact]
    public async Task إعداد_الواجهة_بمظهر_ماركة_للمتجر_الافتراضي_ويدعم_ETag()
    {
        var anonymous = _api.Anonymous();

        var first = await anonymous.GetAsync("/api/storefront/config");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.CacheControl!.NoCache.Should().BeTrue();
        var config = (await first.Content.ReadFromJsonAsync<ConfigBody>(TestApi.Json))!;
        config.Settings.Branding.Colors.Primary.Should().Be("#0F3B3A");
        config.Settings.Branding.Typography.Should().Be("kufi-tajawal");
        config.Settings.DisplayName.Should().Contain("ar", "ماركة");
        config.Settings.Locale.CurrencyDecimals.Should().Be(3);

        var revalidate = new HttpRequestMessage(HttpMethod.Get, "/api/storefront/config");
        revalidate.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        (await anonymous.SendAsync(revalidate)).StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task الوحدة_المعطّلة_تُرفض_في_حالة_الاستخدام_لا_في_النقطة_وحدها()
    {
        var owner = await _api.PlatformOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var productId = await storeApi.CreateProductAsync(await storeApi.AdminAsync());
        (await owner.PutAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/modules", new { modules = new[] { "reviews" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ProblemAsync(await storeApi.Anonymous().GetAsync("/api/coupons/apply?code=ANY&subtotal=10")))
            .Should().Be((HttpStatusCode.NotFound, "ModuleDisabled"));
        var (customer, _) = await storeApi.NewCustomerAsync();
        var order = await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عنوان", items = new[] { new { productId, quantity = 1 } }, couponCode = "ANY",
        });
        (await ProblemAsync(order)).Should().Be((HttpStatusCode.UnprocessableEntity, "ModuleDisabled"));
    }

    [Fact]
    public async Task نطاق_متجر_آخر_لا_يُسرق_والنطاق_الجديد_يُخدَم_فوراً()
    {
        var owner = await _api.PlatformOwnerAsync();
        var taken = await _factory.CreateStoreAsync();
        var target = await _factory.CreateStoreAsync();

        (await ProblemAsync(await owner.PostAsJsonAsync($"/api/platform/tenants/{target.Tenant.Id}/domains",
                new { host = taken.Host.ToUpperInvariant() })))
            .Should().Be((HttpStatusCode.Conflict, "DomainTaken"));

        var fresh = $"n{Guid.NewGuid():N}"[..12] + ".shop.test";
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{target.Tenant.Id}/domains", new { host = fresh })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await _api.Client(fresh).GetFromJsonAsync<ConfigBody>("/api/storefront/config", TestApi.Json))!.Slug
            .Should().Be(target.Tenant.Slug);
    }

    [Fact]
    public async Task العملة_تتغيّر_قبل_النشاط_التجاري_وتُقفل_بعده()
    {
        var owner = await _api.PlatformOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var url = $"/api/platform/tenants/{store.Tenant.Id}";

        (await owner.PutAsJsonAsync(url, new { name = "متجر معدَّل", currency = "USD" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var storeApi = _api.ForStore(store);
        await storeApi.CreateProductAsync(await storeApi.AdminAsync());

        (await ProblemAsync(await owner.PutAsJsonAsync(url, new { name = "متجر معدَّل", currency = "EUR" })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidTenantOperation"));
    }

    [Fact]
    public async Task حسابات_المنصّة_للمالك_وحده_والإيقاف_يُسقط_الجلسة_فوراً()
    {
        var owner = await _api.PlatformOwnerAsync();
        var email = $"ops-{Guid.NewGuid():N}@souq.test";

        (await owner.PostAsJsonAsync("/api/platform/users", new { fullName = "Ops", email, role = "PlatformAdmin" })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        await _factory.DispatchNotificationsAsync();   // رسالة حساب منصّة: نطاق المنصّة في المُرسِل
        var link = _factory.Emails.LastInvitationLinkFor(email);
        new Uri(link).Host.Should().Be(SouqApiFactory.PlatformHost);
        await _api.AcceptInvitationAsync(link, "Ops-Platform-Pass-1");
        var admin = _api.Authorized(
            await _api.TokenOnAsync(SouqApiFactory.PlatformHost, email, "Ops-Platform-Pass-1"), SouqApiFactory.PlatformHost);

        (await admin.GetAsync("/api/platform/tenants")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/platform/stats")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/platform/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ownerId = (await owner.GetFromJsonAsync<TestApi.UserBody>("/api/auth/me", TestApi.Json))!.Id;
        (await ProblemAsync(await owner.PostAsJsonAsync($"/api/platform/users/{ownerId}/status", new { active = false })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "CannotDisableSelf"));

        var accounts = (await owner.GetFromJsonAsync<TestApi.PageBody<AccountBody>>("/api/platform/users?pageSize=100", TestApi.Json))!;
        var adminAccount = accounts.Items.Single(a => a.Email == email);
        adminAccount.Role.Should().Be("PlatformAdmin");
        adminAccount.InvitationPending.Should().BeFalse();
        (await owner.PostAsJsonAsync($"/api/platform/users/{adminAccount.Id}/status", new { active = false })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync("/api/platform/tenants")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task سجلّ_التدقيق_للإضافة_فقط()
    {
        _api.Anonymous();
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditEntries.Add(new AuditEntry(DateTime.UtcNow, AuditAreas.System, "test.appended", null, null, null, null, null, null, null, null));
        await db.SaveChangesAsync();

        db.Remove(await db.AuditEntries.OrderByDescending(a => a.Id).FirstAsync());
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    internal static object SettingsBody(string primary, string text = "#1F2933") => new
    {
        displayName = new Dictionary<string, string> { ["en"] = "Acme Store", ["ar"] = "متجر أكمي" },
        locale = new { defaultCulture = "en", enabledCultures = new[] { "en", "ar" }, timeZone = "UTC" },
        branding = new
        {
            colors = new { primary, secondary = "#EEF2F7", accent = "#F2A541", background = "#FFFFFF", text },
            typography = "ibm-plex", themePreset = "minimal",
        },
        contact = new { email = "hello@acme.test", phone = "+1 555 0100", address = new Dictionary<string, string> { ["en"] = "1 Main St" } },
        social = new[] { new { network = "instagram", url = "https://instagram.com/acme" } },
        seo = new
        {
            title = new Dictionary<string, string> { ["en"] = "Acme" },
            description = new Dictionary<string, string> { ["en"] = "Everything Acme." },
        },
        announcement = new Dictionary<string, string> { ["en"] = "Free returns" },
    };

    internal static HttpContent FileBody(byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return form;
    }

    internal static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    internal sealed record UrlBody(string Url);
    internal sealed record AuditBody(long Id, string Area, string Action, int? TenantId, int? ActorUserId, string? ActorRole, string? TargetId);
    internal sealed record AccountBody(int Id, string Email, string Role, string Status, bool InvitationPending);
    internal sealed record ConfigBody(string Slug, string Status, SettingsView Settings, List<string> Modules);
    internal sealed record SettingsView(Dictionary<string, string> DisplayName, LocaleView Locale, BrandingView Branding);
    internal sealed record LocaleView(string Currency, int CurrencyDecimals, List<string> EnabledCultures);
    internal sealed record BrandingView(ColorsView Colors, string Typography, string? LogoUrl, string? FaviconUrl);
    internal sealed record ColorsView(string Primary, string OnPrimary);
}
