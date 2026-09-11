using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Souq.Domain.Common;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// حدود الصلاحيات تُثبَت بالـ HTTP الحقيقي (المصادقة + الوسطاء + السمات) لا بالافتراض.
// الاختبار الأول يكتشف كل نقطة إدارية من بيانات التوجيه نفسها — أي نقطة جديدة تُضاف
// لاحقاً تُغطّى تلقائياً، فلا يُنسى حماية نقطة إدارية جديدة. في المرحلة 2 يُضاف
// لنفس النمط عزل المستأجرين (مستأجر B يطلب موارد A ⇒ 404).
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class AuthorizationBoundaryTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public AuthorizationBoundaryTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task كل_نقطة_إدارية_ترفض_الزائر_بـ_401_والعميل_بـ_403()
    {
        var anonymous = _api.Anonymous(); // يُقلع الخادم ليُبنى جدول التوجيه
        var (customer, _) = await _api.NewCustomerAsync();

        var adminEndpoints = AdminOnlyEndpoints().ToList();
        adminEndpoints.Should().HaveCountGreaterThanOrEqualTo(15, "يجب ألّا ينجح الاختبار فارغاً");

        foreach (var (method, url, contentType) in adminEndpoints)
        {
            (await Send(anonymous, method, url, contentType)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"{method} {url} للزائر");
            (await Send(customer, method, url, contentType)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{method} {url} للعميل");
        }
    }

    [Fact]
    public async Task عميل_لا_يرى_طلب_عميل_آخر_ولا_يؤكّد_دفعه_والرد_404_لا_403()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var (owner, _) = await _api.NewCustomerAsync();
        var (intruder, _) = await _api.NewCustomerAsync();

        var created = await _api.PlaceOrderAsync(owner, productId, 1);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await created.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;

        (await intruder.GetAsync($"/api/orders/{orderId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PostAsync($"/api/orders/{orderId}/confirm-payment", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/orders/{orderId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task طلبات_العميل_الخاصة_لا_تتضمّن_طلبات_غيره()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var (first, _) = await _api.NewCustomerAsync();
        var (second, _) = await _api.NewCustomerAsync();
        await _api.PlaceOrderAsync(first, productId, 1);

        var secondOrders = await second.GetFromJsonAsync<List<TestApi.IdBody>>("/api/orders/mine", TestApi.Json);

        secondOrders.Should().BeEmpty();
    }

    private IEnumerable<(string Method, string Url, string? ContentType)> AdminOnlyEndpoints()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        foreach (var endpoint in endpoints)
        {
            var requiresAdmin = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(a => a.Roles?.Split(',').Contains(Roles.Admin) == true);
            var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (!requiresAdmin || allowsAnonymous) continue;

            var url = "/" + Regex.Replace(endpoint.RoutePattern.RawText!, @"\{[^}]+\}", "1");
            // نقاط الرفع تقبل multipart فقط: نرسل النوع الذي تعلنه كي يصل الطلب لطبقة
            // الصلاحيات نفسها — وإلا رُفض بـ 415 أثناء اختيار النقطة قبل أي فحص صلاحية.
            var contentType = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.FirstOrDefault();
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                yield return (method, url, contentType);
        }
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string url, string? contentType)
    {
        HttpContent? content = method is "GET" or "DELETE" ? null
            : contentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true
                ? new MultipartFormDataContent()
                : new StringContent("{}", Encoding.UTF8, "application/json");
        return client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = content });
    }
}
