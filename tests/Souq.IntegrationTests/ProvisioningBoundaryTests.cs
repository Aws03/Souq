using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.Domain.Platform;
using Souq.IntegrationTests.Infrastructure;
using static Souq.IntegrationTests.PlatformAdministrationTests;

namespace Souq.IntegrationTests;

// ============================================================================
// تجهيز متجر من المنصّة (المرحلة 18) — ما يحتاجه معالج التجهيز ولم يكن مغطّى:
//   • الخيارات على مضيف المنصّة هي خيارات محرّر المتجر نفسها، لا نسخة.
//   • مضيف المنصّة لا يُربط نطاقاً لمتجر.
//   • قائمة المتاجر تقول هل لمتجرٍ مدير، ومتى صار له.
//   • حساب المنصّة لا يعمل مديراً لمتجر جهّزه — على مضيف المتجر توكنه مرفوض.
//   • المتجر الجديد معزول منذ لحظته الأولى: مديره لا يرى شيئاً من المتجر الافتراضي.
// دورة التجهيز الكاملة عبر HTTP مغطّاة في PlatformAdministrationTests؛ هنا حدودها.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ProvisioningBoundaryTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public ProvisioningBoundaryTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task خيارات_التجهيز_تحمل_خيارات_محرّر_المتجر_نفسها_والوحدات_والحدود()
    {
        var owner = await _api.PlatformOwnerAsync();
        var options = (await owner.GetFromJsonAsync<OptionsBody>("/api/platform/tenants/options", TestApi.Json))!;

        options.Modules.Should().Equal(StoreModules.All);
        options.ReservedSlugs.Should().BeEquivalentTo(Tenant.ReservedSlugs);
        options.Statuses.Should().Equal("Provisioning", "Active", "Suspended", "Archived");
        options.Limits.SlugMax.Should().Be(Tenant.SlugMaxLength);

        // المحرّر نفسه على مضيف المتجر: القوائم متطابقة حرفياً.
        var storeAdmin = await _api.AdminAsync();
        var storeOptions = await storeAdmin.GetStringAsync("/api/admin/store/settings/options");
        var platformSettings = System.Text.Json.JsonSerializer.Serialize(options.Settings, TestApi.Json);
        System.Text.Json.JsonDocument.Parse(storeOptions).RootElement.GetRawText()
            .Should().Be(System.Text.Json.JsonDocument.Parse(platformSettings).RootElement.GetRawText());
    }

    [Fact]
    public async Task مضيف_المنصّة_لا_يُربط_نطاقاً_لمتجر()
    {
        var owner = await _api.PlatformOwnerAsync();
        var tenantId = await CreateAsync(owner);

        (await ProblemAsync(await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/domains",
                new { host = SouqApiFactory.PlatformHost.ToUpperInvariant() })))
            .Should().Be((HttpStatusCode.Conflict, "DomainReserved"));

        var detail = (await owner.GetFromJsonAsync<DetailBody>($"/api/platform/tenants/{tenantId}", TestApi.Json))!;
        detail.Domains.Should().BeEmpty();
    }

    [Fact]
    public async Task القائمة_تقول_هل_للمتجر_مدير_ثم_حين_يقبل_دعوته()
    {
        var owner = await _api.PlatformOwnerAsync();
        var (tenantId, slug, host) = await CreateWithDomainAsync(owner);

        (await RowAsync(owner, slug)).Should().Match<SummaryBody>(r => r.ActiveAdmins == 0 && r.PendingAdminInvitations == 0);

        var email = $"boss-{Guid.NewGuid():N}@provision.test";
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/admins", new { fullName = "Boss", email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowAsync(owner, slug)).Should().Match<SummaryBody>(r => r.ActiveAdmins == 0 && r.PendingAdminInvitations == 1);

        await _factory.DispatchNotificationsAsync();
        await _api.AcceptInvitationAsync(_factory.Emails.LastInvitationLinkFor(email), "Provision-Boss-1");
        var row = await RowAsync(owner, slug);
        (row.ActiveAdmins, row.PendingAdminInvitations, row.PrimaryHost).Should().Be((1, 0, host));
    }

    [Fact]
    public async Task حساب_المنصّة_لا_يعمل_مديراً_للمتجر_الذي_جهّزه_والمتجر_معزول_منذ_لحظته_الأولى()
    {
        var owner = await _api.PlatformOwnerAsync();
        var (tenantId, _, host) = await CreateWithDomainAsync(owner);
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/status", new { action = "Activate" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // (1) توكن المالك على مضيف المتجر: لا tid يطابق المتجر ⇒ 401 قبل أي معالج، على كل سطح إداري.
        var ownerToken = await _api.TokenOnAsync(SouqApiFactory.PlatformHost, SouqApiFactory.PlatformOwnerEmail,
            SouqApiFactory.PlatformOwnerPassword);
        var ownerOnStore = _api.Authorized(ownerToken, host);
        foreach (var path in new[] { "/api/admin/store/settings", "/api/admin/staff", "/api/admin/reports/dashboard", "/api/admin/customers" })
            (await ownerOnStore.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);

        // ولا يدخل بكلمة مروره على مضيف المتجر: حسابات المنصّة ليست حسابات متجر.
        (await _api.Client(host).PostAsJsonAsync("/api/auth/login",
                new { email = SouqApiFactory.PlatformOwnerEmail, password = SouqApiFactory.PlatformOwnerPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // (2) مدير المتجر الجديد: يرى متجره فارغاً، ولا يصل إلى صفّ واحد من المتجر الافتراضي.
        var email = $"boss-{Guid.NewGuid():N}@provision.test";
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/admins", new { fullName = "Boss", email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.DispatchNotificationsAsync();
        await _api.AcceptInvitationAsync(_factory.Emails.LastInvitationLinkFor(email), "Provision-Boss-1");
        var boss = _api.Authorized(await _api.TokenOnAsync(host, email, "Provision-Boss-1"), host);

        var defaultProducts = (await _api.Anonymous().GetFromJsonAsync<TestApi.PageBody<IdOnly>>("/api/products?pageSize=1", TestApi.Json))!;
        defaultProducts.Items.Should().NotBeEmpty("المتجر الافتراضي يجب أن يحمل منتجاً كي يعني الفحص شيئاً");

        (await boss.GetFromJsonAsync<TestApi.PageBody<IdOnly>>("/api/admin/products?pageSize=50", TestApi.Json))!
            .Items.Should().BeEmpty();
        (await boss.GetFromJsonAsync<TestApi.PageBody<IdOnly>>("/api/admin/customers?pageSize=50", TestApi.Json))!
            .Items.Should().BeEmpty();
        (await boss.GetAsync($"/api/admin/products/{defaultProducts.Items[0].Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // (3) وتوكنه لا يصلح على المتجر الافتراضي ولا على مضيف المنصّة.
        var bossToken = await _api.TokenOnAsync(host, email, "Provision-Boss-1");
        (await _api.Authorized(bossToken, SouqApiFactory.DefaultHost).GetAsync("/api/admin/products")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await _api.Authorized(bossToken, SouqApiFactory.PlatformHost).GetAsync("/api/platform/tenants")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<int> CreateAsync(HttpClient owner)
    {
        var slug = $"prov{Guid.NewGuid():N}"[..16];
        var created = await owner.PostAsJsonAsync("/api/platform/tenants",
            new { name = "Provisioned", slug, currency = "USD", defaultCulture = "en", timeZone = "UTC" });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
    }

    private static async Task<(int Id, string Slug, string Host)> CreateWithDomainAsync(HttpClient owner)
    {
        var id = await CreateAsync(owner);
        var detail = (await owner.GetFromJsonAsync<DetailBody>($"/api/platform/tenants/{id}", TestApi.Json))!;
        var host = $"{detail.Slug}.provision.test";
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{id}/domains", new { host })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        return (id, detail.Slug, host);
    }

    private static async Task<SummaryBody> RowAsync(HttpClient owner, string slug) =>
        (await owner.GetFromJsonAsync<TestApi.PageBody<SummaryBody>>($"/api/platform/tenants?search={slug}", TestApi.Json))!
        .Items.Single(r => r.Slug == slug);

    private sealed record OptionsBody(
        object Settings, List<string> Modules, List<string> Statuses, List<string> ReservedSlugs, LimitsBody Limits);
    private sealed record LimitsBody(int SlugMax);
    private sealed record DetailBody(int Id, string Slug, List<DomainBody> Domains);
    private sealed record DomainBody(string Host);
    private sealed record SummaryBody(string Slug, string? PrimaryHost, int ActiveAdmins, int PendingAdminInvitations);
    private sealed record IdOnly(int Id);
}
