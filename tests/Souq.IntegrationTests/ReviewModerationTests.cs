using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الإشراف على التقييمات (المرحلة 13) عبر HTTP وSQL Server الحقيقيين: متجر جديد يبدأ بالإشراف — تقييم المشتري الموثَّق معلّق لا
// يُعرض ولا يدخل الإجماليات حتى يعتمده المشرف، والرفض يخفيه بملاحظة للإدارة. سياسة الاعتماد التلقائي تسري على التقييم التالي،
// والوحدتان المعطّلتان تغلقان العرض والإشراف والإعداد والمفضّلة (404 ModuleDisabled). كل اختبار في متجر جديد: المتجر الافتراضي
// ينشر فوراً (أبقته الهجرة كما كان) وتعتمد عليه اختبارات أخرى.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ReviewModerationTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public ReviewModerationTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task تقييم_متجر_بالإشراف_معلّق_حتى_يُعتمد_والرفض_يخفيه_والإجماليات_من_المعتمد_وحده()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await store.AdminAsync();
        var productId = await store.CreateProductAsync(admin, price: 10m, stock: 10);
        var (happy, _) = await store.NewCustomerAsync();
        var (unhappy, _) = await store.NewCustomerAsync();
        await DeliverAsync(store, happy, admin, productId);
        await DeliverAsync(store, unhappy, admin, productId);

        var five = await PostReviewAsync(happy, productId, 5, "ممتاز");
        var two = await PostReviewAsync(unhappy, productId, 2, "متوسط");
        (five.Status, two.Status).Should().Be(("Pending", "Pending"));

        var hidden = await PublicAsync(store, productId);
        (hidden.TotalCount, hidden.AverageRating).Should().Be((0, 0d));
        hidden.Distribution.Should().HaveCount(5).And.OnlyContain(d => d.Count == 0);

        // الطابور: الاسم كاملاً للمشرف (العامة ترى الاسم الأول فقط).
        var queue = (await admin.GetFromJsonAsync<TestApi.PageBody<AdminReviewBody>>(
            $"/api/admin/reviews?status=Pending&productId={productId}", TestApi.Json))!;
        queue.Items.Select(r => r.Id).Should().BeEquivalentTo(new[] { five.Id, two.Id });
        queue.Items.Should().OnlyContain(r => r.CustomerName == "عميل اختبار" && r.ProductId == productId && r.Status == "Pending");

        (await admin.PostAsync($"/api/admin/reviews/{five.Id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync($"/api/admin/reviews/{two.Id}/reject", new { note = "لغة غير لائقة" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var shown = await PublicAsync(store, productId);
        (shown.TotalCount, shown.AverageRating).Should().Be((1, 5d));
        shown.Items.Select(r => r.Id).Should().Equal(five.Id);
        shown.Distribution.Should().Equal(new RatingCount(5, 1), new RatingCount(4, 0), new RatingCount(3, 0), new RatingCount(2, 0),
            new RatingCount(1, 0));

        var rejected = (await admin.GetFromJsonAsync<TestApi.PageBody<AdminReviewBody>>("/api/admin/reviews?status=Rejected", TestApi.Json))!;
        rejected.Items.Should().ContainSingle(r => r.Id == two.Id && r.ModerationNote == "لغة غير لائقة" && r.ModeratedAt != null);

        // المرفوض يُعاد اعتماده فيدخل الإجماليات.
        (await admin.PostAsync($"/api/admin/reviews/{two.Id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var both = await PublicAsync(store, productId);
        (both.TotalCount, both.AverageRating).Should().Be((2, 3.5d));

        // تقييم غير موجود في هذا المتجر ⇒ 404؛ حالة غير معروفة في الفلتر ⇒ 400.
        (await ProblemAsync(await admin.PostAsync("/api/admin/reviews/999999/approve", null))).Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await admin.GetAsync("/api/admin/reviews?status=Deleted")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task الاعتماد_التلقائي_يُضبط_من_الإعداد_ويسري_على_التقييم_التالي()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await store.AdminAsync();
        (await admin.GetFromJsonAsync<SettingsBody>("/api/admin/reviews/settings", TestApi.Json))!.AutoApprove
            .Should().BeFalse("المتجر الجديد يبدأ بالإشراف");

        (await admin.PutAsJsonAsync("/api/admin/reviews/settings", new { autoApprove = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<SettingsBody>("/api/admin/reviews/settings", TestApi.Json))!.AutoApprove.Should().BeTrue();

        var productId = await store.CreateProductAsync(admin, stock: 3);
        var (customer, _) = await store.NewCustomerAsync();
        await DeliverAsync(store, customer, admin, productId);

        (await PostReviewAsync(customer, productId, 4, "جيد")).Status.Should().Be("Approved");
        var published = await PublicAsync(store, productId);
        (published.TotalCount, published.AverageRating).Should().Be((1, 4d));
    }

    [Fact]
    public async Task الموظّف_يشرف_ولا_يغيّر_سياسة_النشر()
    {
        // reviews.moderate للموظّف؛ تغيير السياسة يلزمه store.settings.manage أيضاً (مدير المتجر).
        var created = await _factory.CreateStoreAsync();
        var store = _api.ForStore(created);
        var staffEmail = await _factory.CreateStoreUserAsync(created.Tenant, Roles.TenantStaff);
        var staff = await store.LoginAsync(staffEmail, SouqApiFactory.StoreAdminPassword);

        (await staff.GetAsync("/api/admin/reviews")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.GetFromJsonAsync<SettingsBody>("/api/admin/reviews/settings", TestApi.Json))!.AutoApprove.Should().BeFalse();
        (await staff.PutAsJsonAsync("/api/admin/reviews/settings", new { autoApprove = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await store.AdminAsync();
        (await admin.GetFromJsonAsync<SettingsBody>("/api/admin/reviews/settings", TestApi.Json))!.AutoApprove.Should().BeFalse();
    }

    [Fact]
    public async Task الوحدتان_المعطّلتان_تغلقان_العرض_والإشراف_والإعداد_والمفضّلة()
    {
        var owner = await _api.PlatformOwnerAsync();
        var created = await _factory.CreateStoreAsync();
        var store = _api.ForStore(created);
        var admin = await store.AdminAsync();
        var (customer, _) = await store.NewCustomerAsync();
        var productId = await store.CreateProductAsync(admin);
        (await owner.PutAsJsonAsync($"/api/platform/tenants/{created.Tenant.Id}/modules", new { modules = new[] { "promotions" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (HttpClient Client, HttpMethod Method, string Url)[] closed =
        [
            (store.Anonymous(), HttpMethod.Get, $"/api/products/{productId}/reviews"),
            (customer, HttpMethod.Post, $"/api/products/{productId}/reviews"),
            (admin, HttpMethod.Get, "/api/admin/reviews"),
            (admin, HttpMethod.Post, "/api/admin/reviews/1/approve"),
            (admin, HttpMethod.Get, "/api/admin/reviews/settings"),
            (admin, HttpMethod.Put, "/api/admin/reviews/settings"),
            (customer, HttpMethod.Get, "/api/wishlist"),
            (customer, HttpMethod.Put, $"/api/wishlist/{productId}"),
            (customer, HttpMethod.Post, "/api/wishlist/merge"),
        ];
        foreach (var (client, method, url) in closed)
        {
            var request = new HttpRequestMessage(method, url) { Content = method == HttpMethod.Get ? null : JsonContent.Create(new { }) };
            (await ProblemAsync(await client.SendAsync(request))).Should().Be((HttpStatusCode.NotFound, "ModuleDisabled"), $"{method} {url}");
        }

        // إعادة التفعيل تفتحها فوراً على هذه النسخة.
        (await owner.PutAsJsonAsync($"/api/platform/tenants/{created.Tenant.Id}/modules", new { modules = new[] { "reviews", "wishlist" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await store.Anonymous().GetAsync($"/api/products/{productId}/reviews")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await customer.GetAsync("/api/wishlist")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // طلب مدفوع بالبوّابة التجريبية ثم شُحن وسُلِّم — شرط أحقّية التقييم.
    private static async Task DeliverAsync(TestApi store, HttpClient customer, HttpClient admin, int productId)
    {
        var created = await store.PlaceOrderAsync(customer, productId, 1);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var orderId = (await created.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Ship" })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Deliver" })).EnsureSuccessStatusCode();
    }

    private static async Task<CreatedReviewBody> PostReviewAsync(HttpClient customer, int productId, int rating, string comment)
    {
        var response = await customer.PostAsJsonAsync($"/api/products/{productId}/reviews", new { rating, comment });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedReviewBody>(TestApi.Json))!;
    }

    private static async Task<ReviewsBody> PublicAsync(TestApi store, int productId) =>
        (await store.Anonymous().GetFromJsonAsync<ReviewsBody>($"/api/products/{productId}/reviews", TestApi.Json))!;

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record CreatedReviewBody(int Id, string Status);
    private sealed record ReviewBody(int Id, string CustomerName, int Rating);
    private sealed record RatingCount(int Rating, int Count);
    private sealed record ReviewsBody(List<ReviewBody> Items, int TotalCount, double AverageRating, List<RatingCount> Distribution);
    private sealed record AdminReviewBody(
        int Id, int ProductId, string ProductName, string CustomerName, int Rating, string Status, DateTime? ModeratedAt, string? ModerationNote);
    private sealed record SettingsBody(bool AutoApprove);
}
