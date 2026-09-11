using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// مصفوفة الأدوار × النقاط عبر HTTP الحقيقي (ADR-0010): لكل دور ما يسمح به جدول RolePermissions فقط.
// "مسموح" = لا 401 ولا 403 (حالة الاستخدام قد تقول 404/409/422 لأسبابها). زائر ⇒ 401، دور بلا صلاحية ⇒ 403.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class AuthorizationMatrixTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public AuthorizationMatrixTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task كل_دور_يصل_لما_يسمح_به_جدول_الصلاحيات_فقط()
    {
        var tenant = await _factory.DefaultTenantAsync();
        var admin = await _api.AdminAsync();
        var staff = await _api.LoginAsync(
            await _factory.CreateStoreUserAsync(tenant, Roles.TenantStaff), SouqApiFactory.StoreAdminPassword);
        var (customer, _) = await _api.NewCustomerAsync();
        var actors = new (string Role, HttpClient Client)[]
        {
            (Roles.TenantAdmin, admin), (Roles.TenantStaff, staff), (Roles.Customer, customer), ("Anonymous", _api.Anonymous()),
        };

        var cases = new (string Method, string Url, Func<object?> Body, string[] Allowed)[]
        {
            ("POST", "/api/categories", () => TestApi.CategoryBody($"mx-{Guid.NewGuid():N}"[..20], "مصفوفة"),
                [Roles.TenantAdmin, Roles.TenantStaff]),
            ("GET", "/api/orders", () => null, [Roles.TenantAdmin, Roles.TenantStaff]),
            ("GET", "/api/admin/inventory", () => null, [Roles.TenantAdmin, Roles.TenantStaff]),
            ("POST", "/api/coupons", () => new { code = $"MX{Guid.NewGuid():N}"[..12], type = "Percentage", value = 5m },
                [Roles.TenantAdmin]),
            ("GET", "/api/coupons", () => null, [Roles.TenantAdmin]),
            ("GET", "/api/auth/me", () => null, [Roles.TenantAdmin, Roles.TenantStaff, Roles.Customer]),
        };

        foreach (var (method, url, body, allowed) in cases)
        foreach (var (role, client) in actors)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (body() is { } payload) request.Content = JsonContent.Create(payload, options: TestApi.Json);
            var status = (await client.SendAsync(request)).StatusCode;

            var expectation = $"{role} → {method} {url}";
            if (allowed.Contains(role))
                status.Should().NotBe(HttpStatusCode.Unauthorized, expectation).And.NotBe(HttpStatusCode.Forbidden, expectation);
            else
                status.Should().Be(role == "Anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, expectation);
        }
    }

    [Fact]
    public async Task الموظّف_بلا_ملف_عميل_لا_يشتري_ولا_يرى_طلبات_شخصية()
    {
        var tenant = await _factory.DefaultTenantAsync();
        var staff = await _api.LoginAsync(
            await _factory.CreateStoreUserAsync(tenant, Roles.TenantStaff), SouqApiFactory.StoreAdminPassword);
        var productId = await _api.CreateProductAsync(await _api.AdminAsync());

        var order = await _api.PlaceOrderAsync(staff, productId, 1);
        order.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await order.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("CustomerAccountRequired");

        (await staff.GetAsync("/api/orders/mine")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
