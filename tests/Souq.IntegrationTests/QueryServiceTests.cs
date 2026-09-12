using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// خدمات القراءة على SQL Server الحقيقي (ADR-0008): الترقيم والترتيب الحتمي والتصفية والإسقاط
// سلوك SQL نفسه، فلا يُثبَت بالمحاكاة. كل اختبار يعزل بياناته في فئة جديدة فريدة.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class QueryServiceTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public QueryServiceTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الترقيم_يعيد_العدد_الإجمالي_وبيانات_التنقّل()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        for (var i = 0; i < 5; i++) await _api.CreateProductAsync(admin, categoryId: category);

        var page = await Products($"categoryIds={category}&pageSize=2&page=2");

        page.TotalCount.Should().Be(5);
        page.Items.Should().HaveCount(2);
        page.PageNumber.Should().Be(2);
        page.TotalPages.Should().Be(3);
        page.HasNext.Should().BeTrue();
        page.HasPrevious.Should().BeTrue();
    }

    [Fact]
    public async Task الترتيب_حتمي_عند_تساوي_المفتاح_فلا_تكرار_ولا_فقدان_بين_الصفحات()
    {
        // خمسة منتجات بالسعر نفسه: بلا كاسر تعادل يعيد SQL Server الصفوف المتساوية بأي ترتيب،
        // فيظهر منتج في صفحتين ويختفي آخر.
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var created = new List<int>();
        for (var i = 0; i < 5; i++) created.Add(await _api.CreateProductAsync(admin, price: 9.5m, categoryId: category));

        var seen = new List<int>();
        for (var p = 1; p <= 3; p++)
            seen.AddRange((await Products($"categoryIds={category}&sortBy=PriceAsc&pageSize=2&page={p}")).Items.Select(x => x.Id));

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(created);
        seen.Should().BeInDescendingOrder("المعرّف يكسر التعادل (الأحدث أولاً)");
    }

    [Fact]
    public async Task التصفية_بالسعر_والكلمة_والترتيب_بالسعر()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var token = $"tok{Guid.NewGuid():N}"[..12];
        var cheap = await _api.CreateProductAsync(admin, price: 5m, categoryId: category);
        var middle = await _api.CreateProductAsync(admin, price: 15m, categoryId: category, name: $"منتج {token}");
        var expensive = await _api.CreateProductAsync(admin, price: 25m, categoryId: category);

        (await Products($"categoryIds={category}&minPrice=10&maxPrice=20")).Items.Select(p => p.Id).Should().Equal(middle);
        (await Products($"categoryIds={category}&keyword={token}")).Items.Select(p => p.Id).Should().Equal(middle);
        (await Products($"categoryIds={category}&sortBy=PriceDesc")).Items.Select(p => p.Id).Should().Equal(expensive, middle, cheap);
        (await Products($"categoryIds={category}&sortBy=PriceAsc")).Items.Select(p => p.Id).Should().Equal(cheap, middle, expensive);
    }

    [Fact]
    public async Task المنتج_المعطّل_لا_يظهر_في_القائمة_ولا_التفاصيل_ولا_ذات_الصلة()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var kept = await _api.CreateProductAsync(admin, categoryId: category);
        var disabled = await _api.CreateProductAsync(admin, categoryId: category);
        (await admin.DeleteAsync($"/api/products/{disabled}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Products($"categoryIds={category}")).Items.Select(p => p.Id).Should().Equal(kept);
        (await _api.Anonymous().GetAsync($"/api/products/{disabled}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var related = await _api.Anonymous().GetFromJsonAsync<List<TestApi.IdBody>>($"/api/products/{kept}/related?count=24", TestApi.Json);
        related!.Select(p => p.Id).Should().NotContain([kept, disabled]);
    }

    [Theory]
    [InlineData("/api/orders/mine?pageSize=101", "customer")]
    [InlineData("/api/admin/inventory?pageSize=101", "admin")]
    [InlineData("/api/admin/inventory/low-stock?page=0", "admin")]
    [InlineData("/api/admin/inventory/1/movements?pageSize=0", "admin")]
    [InlineData("/api/orders/mine?page=1000&pageSize=100", "customer")]   // F-18: إزاحة 99,900
    public async Task القوائم_التي_كانت_بلا_حدّ_ترفض_حجم_صفحة_خارج_الحدود(string url, string who)
    {
        var client = who == "admin" ? await _api.AdminAsync() : (await _api.NewCustomerAsync()).Client;

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // F-18: حجم الصفحة كان محدوداً والإزاحة لا. /api/products مجهول الهوية وبلا حدّ معدّل، فيقرأ page=100000 ملايين
    // الصفوف ويرميها — وبترتيب "الأكثر مبيعاً" يجمع فوقها تاريخ الطلبات المُسلَّمة كلّه (F-17).
    [Fact]
    public async Task إزاحة_ترقيم_عميقة_تُرفض_والتصفّح_الطبيعي_يمرّ()
    {
        var anonymous = _api.Anonymous();

        (await anonymous.GetAsync("/api/products?page=100000&pageSize=100")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "الإزاحة وحدها ~10 ملايين صفّ");
        (await anonymous.GetAsync("/api/products?page=1&pageSize=12")).StatusCode
            .Should().Be(HttpStatusCode.OK, "الحدّ لا يمسّ التصفّح الحقيقي");
    }

    [Fact]
    public async Task طلباتي_مرقّمة_والأحدث_أولاً_بإجماليات_محسوبة_في_القاعدة()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10.125m, stock: 20);
        var (customer, _) = await _api.NewCustomerAsync();
        for (var quantity = 1; quantity <= 3; quantity++)
            (await _api.PlaceOrderAsync(customer, productId, quantity)).StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await customer.GetFromJsonAsync<TestApi.PageBody<OrderSummaryBody>>("/api/orders/mine?pageSize=2", TestApi.Json);

        page!.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
        page.Items[0].TotalAmount.Should().Be(30.375m);   // الأحدث: 3 × 10.125 — دقّة الدينار محفوظة
        page.Items[0].Currency.Should().Be("JOD");
        page.Items[0].ItemCount.Should().Be(1);
        page.Items[1].TotalAmount.Should().Be(20.250m);
    }

    [Fact]
    public async Task سجلّ_الحركة_مرقّم_والأحدث_أولاً()
    {
        // المرحلة 6: الطلب يحجز والدفع وحده يسجّل البيع — السجلّ: توريد ثم بيعان بعد الدفع، الأحدث أولاً.
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, stock: 5);          // حركة توريد
        var (customer, _) = await _api.NewCustomerAsync();
        foreach (var quantity in new[] { 1, 2 })
        {
            var placed = await _api.PlaceOrderAsync(customer, productId, quantity);
            var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
            (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);                       // بيع
        }

        var page = await admin.GetFromJsonAsync<TestApi.PageBody<MovementBody>>(
            $"/api/admin/inventory/{productId}/movements?pageSize=2", TestApi.Json);

        page!.TotalCount.Should().Be(3);
        page.Items.Select(m => (m.Type, m.QuantityChange)).Should().Equal(("Sale", -2), ("Sale", -1));
    }

    [Fact]
    public async Task المنخفض_المخزون_مرقّم_ويعطي_العدد_للشارة()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, stock: 1);          // حدّ التنبيه الافتراضي 5

        var badge = await admin.GetFromJsonAsync<TestApi.PageBody<InventoryBody>>("/api/admin/inventory/low-stock?pageSize=1", TestApi.Json);
        var all = await admin.GetFromJsonAsync<TestApi.PageBody<InventoryBody>>("/api/admin/inventory/low-stock?pageSize=100", TestApi.Json);

        badge!.Items.Should().HaveCount(1);
        badge.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        all!.Items.Should().Contain(i => i.Id == productId && i.IsLowStock);
    }

    [Fact]
    public async Task قائمة_التقييمات_بعدد_استعلامات_ثابت_والاسم_الأول_فقط()
    {
        // Phase 0 C13: كان المعالج يجلب كل عميل على حدة لكل تقييم (N+1).
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, stock: 10);
        for (var i = 0; i < 3; i++)
        {
            var (customer, _) = await _api.NewCustomerAsync();
            await DeliverOrderAsync(customer, admin, productId);
            (await customer.PostAsJsonAsync($"/api/products/{productId}/reviews", new { rating = 4, comment = "جيد" }))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var before = ExecutedCommands();
        var reviews = await _api.Anonymous().GetFromJsonAsync<ReviewsBody>($"/api/products/{productId}/reviews?pageSize=10", TestApi.Json);
        var commands = ExecutedCommands() - before;

        reviews!.TotalCount.Should().Be(3);
        reviews.AverageRating.Should().Be(4);
        reviews.Items.Should().OnlyContain(r => r.CustomerName == "عميل"); // "عميل اختبار" ⇒ الاسم الأول فقط
        commands.Should().BeLessThanOrEqualTo(3, "العدد + المتوسط + الصفحة، أياً كان عدد التقييمات");
    }

    [Fact]
    public async Task طلب_مدفوع_لم_يُسلَّم_بعد_لا_يتيح_التقييم()
    {
        // انتقلت من اختبار الوحدة: "مُسلَّم يحوي المنتج" صار استعلام SQL (FindDeliveredOrderIdContainingAsync).
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        var orderId = await PlaceAndPayAsync(customer, productId);
        (await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Ship" })).EnsureSuccessStatusCode();

        var review = await customer.PostAsJsonAsync($"/api/products/{productId}/reviews", new { rating = 5, comment = "رائع" });

        review.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await review.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("NotEligible");
    }

    private async Task<TestApi.PageBody<TestApi.IdBody>> Products(string query) =>
        (await _api.Anonymous().GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>($"/api/products?{query}", TestApi.Json))!;

    private async Task<int> PlaceAndPayAsync(HttpClient customer, int productId)
    {
        var created = await _api.PlaceOrderAsync(customer, productId, 1);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await created.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null)).EnsureSuccessStatusCode(); // البوّابة التجريبية
        return orderId;
    }

    private async Task DeliverOrderAsync(HttpClient customer, HttpClient admin, int productId)
    {
        var orderId = await PlaceAndPayAsync(customer, productId);
        (await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Ship" })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Deliver" })).EnsureSuccessStatusCode();
    }

    private int ExecutedCommands() => _factory.Logs.Messages.Count(m => m.StartsWith("Executed DbCommand", StringComparison.Ordinal));

    private sealed record OrderSummaryBody(int Id, decimal TotalAmount, string Currency, int ItemCount);
    private sealed record MovementBody(string Type, int QuantityChange);
    private sealed record InventoryBody(int Id, bool IsLowStock);
    private sealed record ReviewBody(string CustomerName, int Rating);
    private sealed record ReviewsBody(List<ReviewBody> Items, int TotalCount, double AverageRating);
}
