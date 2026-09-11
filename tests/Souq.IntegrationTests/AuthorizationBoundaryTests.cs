using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Security;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// حدود الصلاحيات تُثبَت بالـ HTTP الحقيقي (المصادقة + الوسطاء + السمات) لا بالافتراض.
// النقاط تُكتشف من بيانات التوجيه نفسها — أي نقطة جديدة تُغطّى تلقائياً:
//   • كل نقطة تعلن قرارها صراحةً (عامة / مُصادَقة / صلاحية) — لا نقطة عامة بالنسيان.
//   • النقاط العامة قائمة مراجَعة هنا؛ جعل نقطة عامة قرار واعٍ يعدّل هذه القائمة.
//   • كل نقطة بصلاحية: زائر ⇒ 401، عميل ⇒ 403.
// في المرحلة 2 يُضاف لنفس النمط عزل المستأجرين (مستأجر B يطلب موارد A ⇒ 404).
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class AuthorizationBoundaryTests
{
    private static readonly HashSet<string> ReviewedPublicEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST api/Auth/register", "POST api/Auth/login", "POST api/Auth/forgot-password", "POST api/Auth/reset-password",
        "POST api/Auth/refresh", "POST api/Auth/logout", "POST api/Auth/verify-email",
        "GET api/Products", "GET api/Products/{id:int}", "GET api/Products/by-slug/{slug}", "GET api/Products/{id:int}/related",
        "GET api/Categories",
        "GET api/Coupons/apply",
        "GET api/products/{productId:int}/reviews",
        "GET api/Orders/{id:int}/tracking",
        "GET api/payments/config", "POST api/payments/webhook",
        "GET api/storefront/config",
        // السلة (المرحلة 8): للزائر برمز ملف تعريف ارتباط وللعميل بجلسته — لا بيانات غير سلة المتصل نفسه.
        "GET api/basket", "GET api/basket/quote", "POST api/basket/items", "PUT api/basket/items/{productId:int}",
        "DELETE api/basket/items/{productId:int}", "DELETE api/basket",
    };

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public AuthorizationBoundaryTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task كل_نقطة_متجر_محمية_بصلاحية_ترفض_الزائر_بـ_401_والعميل_بـ_403()
    {
        var anonymous = _api.Anonymous(); // يُقلع الخادم ليُبنى جدول التوجيه
        var (customer, _) = await _api.NewCustomerAsync();

        var protectedEndpoints = Endpoints()
            .Where(e => !e.IsPlatform && !e.AllowsAnonymous && e.Policies.Any(IsPermissionPolicy))
            .SelectMany(e => e.Methods.Select(method => (Method: method, Url: SampleUrl(e.Route), e.ContentType)))
            .ToList();
        protectedEndpoints.Should().HaveCountGreaterThanOrEqualTo(15, "يجب ألّا ينجح الاختبار فارغاً");

        foreach (var (method, url, contentType) in protectedEndpoints)
        {
            (await Send(anonymous, method, url, contentType)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"{method} {url} للزائر");
            (await Send(customer, method, url, contentType)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"{method} {url} للعميل");
        }
    }

    [Fact]
    public async Task نقاط_المنصّة_لا_توجد_على_مضيف_متجر_وترفض_كل_ما_ليس_حساب_منصّة()
    {
        // منطقة المنصّة: على مضيف متجر "غير موجودة" حتى لمدير المتجر (لا نكشف وجودها)، وعلى مضيف المنصّة
        // الزائر 401 وتوكن المتجر 401 (لا tid على مضيف المنصّة) — قبل أي معالج.
        var storeAdmin = await _api.AdminAsync();
        var storeAdminOnPlatform = _api.Authorized(await _api.AdminTokenAsync(), SouqApiFactory.PlatformHost);
        var anonymousOnPlatform = _api.Client(SouqApiFactory.PlatformHost);

        var platformEndpoints = Endpoints()
            .Where(e => e.IsPlatform)
            .SelectMany(e => e.Methods.Select(method => (Method: method, Url: SampleUrl(e.Route), e.ContentType)))
            .ToList();
        platformEndpoints.Should().HaveCountGreaterThanOrEqualTo(15, "يجب ألّا ينجح الاختبار فارغاً");
        Endpoints().Where(e => e.IsPlatform).Should().OnlyContain(e => !e.AllowsAnonymous && e.Policies.Any(IsPermissionPolicy),
            "كل نقطة منصّة خلف صلاحية منصّة");

        foreach (var (method, url, contentType) in platformEndpoints)
        {
            (await Send(storeAdmin, method, url, contentType)).StatusCode
                .Should().Be(HttpStatusCode.NotFound, $"{method} {url} على مضيف متجر");
            (await Send(anonymousOnPlatform, method, url, contentType)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"{method} {url} للزائر على مضيف المنصّة");
            (await Send(storeAdminOnPlatform, method, url, contentType)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"{method} {url} بتوكن متجر على مضيف المنصّة");
        }
    }

    [Fact]
    public void كل_نقطة_تعلن_قرار_صلاحيتها_صراحةً()
    {
        _api.Anonymous();

        var undecided = Endpoints()
            .Where(e => !e.AllowsAnonymous && !e.HasAuthorizeData)
            .SelectMany(e => e.Methods.Select(m => $"{m} {e.Route}"))
            .ToList();

        undecided.Should().BeEmpty("كل نقطة إمّا [AllowAnonymous] أو [Authorize]/[HasPermission]");
    }

    [Fact]
    public void النقاط_العامة_هي_القائمة_المراجَعة_فقط()
    {
        _api.Anonymous();

        var publicEndpoints = Endpoints()
            .Where(e => e.AllowsAnonymous)
            .SelectMany(e => e.Methods.Select(m => $"{m} {e.Route}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        publicEndpoints.Should().BeEquivalentTo(ReviewedPublicEndpoints);
    }

    [Fact]
    public void كل_صلاحية_معلنة_على_نقطة_معرّفة_في_جدول_الصلاحيات()
    {
        _api.Anonymous();

        var declared = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .SelectMany(e => e.Metadata.GetOrderedMetadata<HasPermissionAttribute>())
            .Select(a => a.Permission)
            .Distinct()
            .ToList();

        declared.Should().NotBeEmpty();
        declared.Should().OnlyContain(p => Permissions.All.Contains(p));
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
        (await admin.GetAsync($"/api/orders/{orderId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task طلبات_العميل_الخاصة_لا_تتضمّن_طلبات_غيره()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var (first, _) = await _api.NewCustomerAsync();
        var (second, _) = await _api.NewCustomerAsync();
        await _api.PlaceOrderAsync(first, productId, 1);

        var secondOrders = await second.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>("/api/orders/mine", TestApi.Json);

        secondOrders!.Items.Should().BeEmpty();
        secondOrders.TotalCount.Should().Be(0);
    }

    private sealed record EndpointInfo(
        string Route, IReadOnlyList<string> Methods, bool AllowsAnonymous, bool HasAuthorizeData,
        IReadOnlyList<string> Policies, string? ContentType, bool IsPlatform);

    private IEnumerable<EndpointInfo> Endpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e =>
            {
                var authorizeData = e.Metadata.GetOrderedMetadata<IAuthorizeData>();
                return new EndpointInfo(
                    e.RoutePattern.RawText!,
                    e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ToList() ?? ["GET"],
                    e.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                    authorizeData.Count > 0,
                    authorizeData.Select(a => a.Policy).OfType<string>().ToList(),
                    // نقاط الرفع تقبل multipart فقط: نرسل النوع الذي تعلنه كي يصل الطلب لطبقة
                    // الصلاحيات نفسها — وإلا رُفض بـ 415 أثناء اختيار النقطة قبل أي فحص صلاحية.
                    e.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.FirstOrDefault(),
                    e.Metadata.GetMetadata<PlatformEndpointAttribute>() is not null);
            });

    private static bool IsPermissionPolicy(string policy) =>
        policy.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal);

    private static string SampleUrl(string route) => "/" + Regex.Replace(route, @"\{[^}]+\}", "1");

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string url, string? contentType)
    {
        HttpContent? content = method is "GET" or "DELETE" ? null
            : contentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true
                ? new MultipartFormDataContent()
                : new StringContent("{}", Encoding.UTF8, "application/json");
        return client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = content });
    }
}
