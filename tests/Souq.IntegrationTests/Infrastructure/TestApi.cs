using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Souq.Infrastructure.Persistence;

namespace Souq.IntegrationTests.Infrastructure;

// أدوات مشتركة لسيناريوهات HTTP: عملاء مصادَقون، إنشاء منتجات/طلبات، ووصول مباشر
// للقاعدة للتحقّق مما خُزِّن فعلاً (لا ما أعلنه الـ API فقط).
public sealed class TestApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SouqApiFactory _factory;
    public TestApi(SouqApiFactory factory) => _factory = factory;

    public HttpClient Anonymous() => _factory.CreateClient();

    public async Task<HttpClient> AdminAsync() =>
        await LoginAsync(SouqApiFactory.AdminEmail, SouqApiFactory.AdminPassword);

    public async Task<(HttpClient Client, string Email)> NewCustomerAsync()
    {
        var email = $"customer-{Guid.NewGuid():N}@souq.test";
        var response = await Anonymous().PostAsJsonAsync("/api/auth/register",
            new { fullName = "عميل اختبار", email, password = "Customer-Pass-1" });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthBody>(Json);
        return (Authorized(auth!.Token), email);
    }

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthBody>(Json);
        return Authorized(auth!.Token);
    }

    public async Task<int> CreateProductAsync(
        HttpClient admin, decimal price = 10m, int stock = 5, int? categoryId = null, string? name = null)
    {
        categoryId ??= (await admin.GetFromJsonAsync<List<IdBody>>("/api/categories", Json))!.First().Id;
        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            nameAr = name ?? $"منتج {Guid.NewGuid():N}", description = "اختبار", price, stockQuantity = stock,
            imageUrl = "placeholder", categoryId,
        });
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdBody>(Json))!.Id;
    }

    // فئة جديدة فريدة — تعزل قوائم اختبار عن بيانات الاختبارات الأخرى في القاعدة المشتركة.
    public async Task<int> CreateCategoryAsync(HttpClient admin)
    {
        var slug = $"it-{Guid.NewGuid():N}"[..24];
        var response = await admin.PostAsJsonAsync("/api/categories", new { name = $"فئة {slug}", slug });
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdBody>(Json))!.Id;
    }

    public async Task<HttpResponseMessage> PlaceOrderAsync(HttpClient customer, int productId, int quantity) =>
        await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = new[] { new { productId, quantity } },
        });

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private HttpClient Authorized(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // شكل PaginatedList في JSON — عقد كل القوائم المرقّمة.
    public sealed record PageBody<T>(
        List<T> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasNext, bool HasPrevious);

    public sealed record AuthBody(string Token);
    public sealed record IdBody(int Id);
    public sealed record OrderCreatedBody(int OrderId, string Status, decimal TotalAmount);
    // عقد الأخطاء (ADR-0017): RFC 7807 + code + traceId (+ errors لأخطاء التحقّق).
    public sealed record ProblemBody(
        string? Type, string? Title, int? Status, string? Detail, string? Code, string? TraceId,
        Dictionary<string, string[]>? Errors);
}
