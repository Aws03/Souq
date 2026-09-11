using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.Domain.Platform;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// تحديد المستأجر من المضيف عبر الخادم الحقيقي (ADR-0006): مضيف مجهول، متجر موقوف أو قيد التجهيز،
// مضيف المنصّة، ووسائل التطوير المسموحة في Testing ({slug}.localhost، X-Tenant).
[Collection(IntegrationCollection.Name)]
public class TenantResolutionTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public TenantResolutionTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task مضيف_بلا_متجر_يعيد_404_StoreNotFound_بلا_متجر_احتياطي()
    {
        var response = await _api.Client("unknown-store.example").GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(response)).Should().Be("StoreNotFound");
    }

    [Fact]
    public async Task متجر_موقوف_غير_متاح_للزوّار_ولا_للدخول()
    {
        var store = await _factory.CreateStoreAsync(TenantStatus.Suspended);
        var api = _api.ForStore(store);

        var catalog = await api.Anonymous().GetAsync("/api/products");
        catalog.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemCodeAsync(catalog)).Should().Be("StoreUnavailable");

        (await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = store.AdminEmail, password = store.AdminPassword }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task متجر_قيد_التجهيز_مغلق_للزوّار_ومفتوح_لإدارته()
    {
        var store = await _factory.CreateStoreAsync(TenantStatus.Provisioning);
        var api = _api.ForStore(store);

        (await api.Anonymous().GetAsync("/api/products")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var admin = await api.AdminAsync();
        (await admin.GetAsync("/api/coupons")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task مضيف_المنصّة_لا_يخدم_نقاط_المتاجر()
    {
        var response = await _api.Client("admin.localhost").GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task في_الاختبار_يُحدَّد_المتجر_بنطاق_localhost_الفرعي_أو_بترويسة_التطوير()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var productId = await storeApi.CreateProductAsync(await storeApi.AdminAsync());

        (await _api.Client($"{store.Tenant.Slug}.localhost").GetAsync($"/api/products/{productId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var viaHeader = _api.Anonymous();
        viaHeader.DefaultRequestHeaders.Add("X-Tenant", store.Tenant.Slug);
        (await viaHeader.GetAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // localhost بلا ترويسة = المتجر الافتراضي، ولا يرى منتج المتجر الآخر.
        (await _api.Anonymous().GetAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task سطر_سجلّ_الطلب_يحمل_المتجر()
    {
        var store = await _factory.CreateStoreAsync();

        await _api.ForStore(store).Anonymous().GetAsync("/api/categories");

        _factory.Logs.Entries.Should().Contain(e =>
            e.Category.EndsWith("RequestLoggingMiddleware")
            && e.Scope.ContainsKey("TenantId") && Equals(e.Scope["TenantId"], store.Tenant.Id));
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code;
}
