using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Persistence;

namespace Souq.IntegrationTests.Infrastructure;

// أدوات مشتركة لسيناريوهات HTTP: عملاء مصادَقون، إنشاء منتجات/طلبات، ووصول مباشر
// للقاعدة للتحقّق مما خُزِّن فعلاً (لا ما أعلنه الـ API فقط). كل مثيل مربوط بمضيف متجر
// واحد (المتجر الافتراضي على localhost ما لم يُستخدم ForStore) — كما يصل المتصفّح تماماً.
public sealed class TestApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SouqApiFactory _factory;
    private readonly TestStore? _store;

    public TestApi(SouqApiFactory factory) => _factory = factory;

    private TestApi(SouqApiFactory factory, TestStore store)
    {
        _factory = factory; _store = store;
    }

    public string Host => _store?.Host ?? SouqApiFactory.DefaultHost;

    public TestApi ForStore(TestStore store) => new(_factory, store);

    public HttpClient Anonymous() => Client(Host);

    // عميل على مضيف آخر بلا تغيير في المتجر المربوط — لاختبارات "توكن متجر على مضيف غيره".
    public HttpClient Client(string host) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{host}/") });

    // عميل https بحاوية ملفات تعريف ارتباط (رمز التجديد Secure لا يُرسَل على http)؛ handleCookies=false
    // لإرسال ملف تعريف ارتباط يدوياً (إعادة رمز قديم).
    public HttpClient SecureClient(string? host = null, bool handleCookies = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{host ?? Host}/"),
            HandleCookies = handleCookies,
        });

    public async Task<HttpClient> AdminAsync() => _store is null
        ? await LoginAsync(SouqApiFactory.AdminEmail, SouqApiFactory.AdminPassword)
        : await LoginAsync(_store.AdminEmail, _store.AdminPassword);

    public async Task<string> AdminTokenAsync() => _store is null
        ? await TokenAsync(SouqApiFactory.AdminEmail, SouqApiFactory.AdminPassword)
        : await TokenAsync(_store.AdminEmail, _store.AdminPassword);

    // معلَنة لا محشوّة: اختبارٌ يحتاج كلمة المرور الحالية (تغييرها مثلاً) كان سينسخ النصّ ويفترق عنها.
    public const string CustomerPassword = "Customer-Pass-1";

    public async Task<(HttpClient Client, string Email)> NewCustomerAsync(string? email = null)
    {
        email ??= $"customer-{Guid.NewGuid():N}@souq.test";
        var response = await Anonymous().PostAsJsonAsync("/api/auth/register",
            new { fullName = "عميل اختبار", email, password = CustomerPassword });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthBody>(Json);
        return (Authorized(auth!.AccessToken), email);
    }

    public async Task<HttpClient> LoginAsync(string email, string password) =>
        Authorized(await TokenAsync(email, password));

    public async Task<string> TokenAsync(string email, string password)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthBody>(Json))!.AccessToken;
    }

    // دخول على مضيف بعينه (مضيف المنصّة، أو نطاق متجر أنشأته المنصّة للتوّ).
    public async Task<string> TokenOnAsync(string host, string email, string password)
    {
        var response = await Client(host).PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthBody>(Json))!.AccessToken;
    }

    // مالك المنصّة (مبذور من الإعداد الصريح) على مضيف المنصّة — مدخل منطقة المنصّة.
    public async Task<HttpClient> PlatformOwnerAsync() => Authorized(
        await TokenOnAsync(SouqApiFactory.PlatformHost, SouqApiFactory.PlatformOwnerEmail, SouqApiFactory.PlatformOwnerPassword),
        SouqApiFactory.PlatformHost);

    // قبول دعوة كما يفعل المتصفّح: الرمز من الرابط، والطلب على مضيف الرابط نفسه.
    public async Task AcceptInvitationAsync(string invitationLink, string password)
    {
        var link = new Uri(invitationLink);
        var token = Uri.UnescapeDataString(link.Query[(link.Query.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
        var response = await Client(link.Host).PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = password });
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public async Task<int> CreateProductAsync(
        HttpClient admin, decimal price = 10m, int stock = 5, int? categoryId = null, string? name = null)
    {
        categoryId ??= await CreateCategoryAsync(admin);
        var response = await admin.PostAsJsonAsync("/api/products", ProductBody(categoryId.Value, price, stock, name));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdBody>(Json))!.Id;
    }

    // جسم إنشاء منتج بعقد المرحلة 5: نصوص لكل لغة (العربية لغة المتجر الافتراضي)، والمعرّف النصّي يقترحه الخادم.
    public static object ProductBody(
        int categoryId, decimal price = 10m, int stock = 5, string? name = null, string? slug = null, string? sku = null) => new
    {
        categoryId,
        translations = new Dictionary<string, object> { ["ar"] = new { name = name ?? $"منتج {Guid.NewGuid():N}", description = "اختبار" } },
        price,
        stockQuantity = stock,
        slug,
        sku,
    };

    // جسم تعديل منتج (PUT يستبدل الحقول التحريرية كلها؛ المخزون اختياري بحارس التزامن).
    public static object ProductUpdateBody(int categoryId, string slug, decimal price = 10m, string name = "منتج معدّل") => new
    {
        categoryId,
        slug,
        translations = new Dictionary<string, object> { ["ar"] = new { name, description = "اختبار" } },
        price,
    };

    // فئة جديدة فريدة — تعزل قوائم اختبار عن بيانات الاختبارات الأخرى في القاعدة المشتركة.
    public async Task<int> CreateCategoryAsync(HttpClient admin, string? slug = null, int? parentId = null)
    {
        var response = await admin.PostAsJsonAsync("/api/categories", CategoryBody(slug, parentId: parentId));
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdBody>(Json))!.Id;
    }

    public static object CategoryBody(string? slug = null, string? name = null, int? parentId = null)
    {
        slug ??= $"it-{Guid.NewGuid():N}"[..24];
        return new { slug, translations = new Dictionary<string, object> { ["ar"] = new { name = name ?? $"فئة {slug}" } }, parentId };
    }

    // نصوص كتالوج عربية لكيانات تُزرع مباشرة في القاعدة.
    public static Dictionary<string, Souq.Domain.ValueObjects.CatalogText> ArabicText(string name) => new() { ["ar"] = new(name) };

    public async Task<HttpResponseMessage> PlaceOrderAsync(HttpClient customer, int productId, int quantity) =>
        await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = new[] { new { productId, quantity } },
        });

    // وصول مباشر للقاعدة داخل متجر هذا المضيف (كما يراه طلب HTTP عليه).
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = await _factory.TenantScopeAsync(_store?.Tenant);
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task<TenantInfo> TenantAsync() =>
        _store is null ? _factory.DefaultTenantAsync() : Task.FromResult(_store.Tenant);

    public HttpClient Authorized(string token, string? host = null)
    {
        var client = Client(host ?? Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // رمز سلة الزائر من Set-Cookie لاستجابة (المرحلة 8) — لإرساله يدوياً (مضيف آخر، رمز قديم).
    public const string GuestBasketCookie = "souq_basket";

    public static string GuestBasketToken(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith($"{GuestBasketCookie}=", StringComparison.Ordinal));
        return header[(GuestBasketCookie.Length + 1)..header.IndexOf(';')];
    }

    // عقد السلة في JSON (المرحلة 8).
    public sealed record BasketBody(
        List<BasketLineBody> Lines, int ItemCount, string Currency, decimal Subtotal, decimal Discount,
        decimal Shipping, decimal Tax, decimal Total, BasketCouponBody? Coupon, bool ReadyForCheckout);
    public sealed record BasketLineBody(
        int ProductId, int VariantId, string Name, string? ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal,
        bool Sellable, int Available, string? VariantLabel = null);
    public sealed record BasketCouponBody(string Code, bool Applied, string? ErrorCode, string? Message);

    // شكل PaginatedList في JSON — عقد كل القوائم المرقّمة.
    public sealed record PageBody<T>(
        List<T> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasNext, bool HasPrevious);

    public sealed record AuthBody(string AccessToken, DateTime ExpiresAt, UserBody User);
    public sealed record UserBody(
        int Id, string Email, string Role, List<string> Permissions, bool EmailConfirmed, int? CustomerId, string Area);
    public sealed record IdBody(int Id);
    public sealed record OrderCreatedBody(int OrderId, string Status, decimal TotalAmount);
    // عقد الأخطاء (ADR-0017): RFC 7807 + code + traceId (+ errors لأخطاء التحقّق).
    public sealed record ProblemBody(
        string? Type, string? Title, int? Status, string? Detail, string? Code, string? TraceId,
        Dictionary<string, string[]>? Errors);
}
