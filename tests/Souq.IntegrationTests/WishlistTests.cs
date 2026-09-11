using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// مفضّلة العميل على الخادم (المرحلة 13) عبر HTTP وSQL Server الحقيقيين: إضافة متساوية الأثر، الأحدث أولاً بأسعار الكتالوج
// الحيّة، المؤرشف يختفي من العرض ويعود بإعادة نشره، دمج قائمة الزائر يتجاهل المكرّر والمجهول ومنتجات متجر آخر، والمحو يحذفها.
// الوحدة المعطّلة في ReviewModerationTests، ومسارات منتج متجر آخر في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class WishlistTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public WishlistTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الإضافة_بلا_تكرار_والأحدث_أولاً_بالسعر_الحي_والمؤرشف_يختفي_ويعود_والحذف()
    {
        var admin = await _api.AdminAsync();
        var first = await _api.CreateProductAsync(admin, price: 12m, stock: 3, name: "منتج أول");
        var second = await _api.CreateProductAsync(admin, price: 8m, stock: 0);
        var (customer, _) = await _api.NewCustomerAsync();

        (await ReadAsync(await customer.PutAsync($"/api/wishlist/{first}", null))).Items.Select(i => i.Id).Should().Equal(first);
        (await ReadAsync(await customer.PutAsync($"/api/wishlist/{first}", null))).Items.Select(i => i.Id).Should().Equal(first);
        var both = await ReadAsync(await customer.PutAsync($"/api/wishlist/{second}", null));
        both.Items.Select(i => i.Id).Should().Equal(second, first);
        var firstItem = both.Items.Single(i => i.Id == first);
        (firstItem.Price, firstItem.StockQuantity, firstItem.Name, firstItem.Translations["ar"].Name)
            .Should().Be((12m, 3, "منتج أول", "منتج أول"));
        both.Items.Single(i => i.Id == second).StockQuantity.Should().Be(0, "نفد المخزون يبقى في المفضّلة ويُعرض نافداً");

        (await admin.PutAsJsonAsync($"/api/admin/products/{second}/status", new { status = "Archived" })).EnsureSuccessStatusCode();
        (await GetAsync(customer)).Items.Select(i => i.Id).Should().Equal(first);
        (await admin.PutAsJsonAsync($"/api/admin/products/{second}/status", new { status = "Active" })).EnsureSuccessStatusCode();
        (await GetAsync(customer)).Items.Select(i => i.Id).Should().Equal(second, first);

        (await ReadAsync(await customer.DeleteAsync($"/api/wishlist/{first}"))).Items.Select(i => i.Id).Should().Equal(second);
        (await ProblemAsync(await customer.DeleteAsync($"/api/wishlist/{first}"))).Should().Be((HttpStatusCode.NotFound, "NotFound"));
    }

    [Fact]
    public async Task منتج_غير_منشور_أو_غير_موجود_لا_يُضاف_والزائر_والإدارة_بلا_مفضّلة()
    {
        var admin = await _api.AdminAsync();
        var archived = await _api.CreateProductAsync(admin);
        (await admin.PutAsJsonAsync($"/api/admin/products/{archived}/status", new { status = "Archived" })).EnsureSuccessStatusCode();
        var (customer, _) = await _api.NewCustomerAsync();

        (await ProblemAsync(await customer.PutAsync($"/api/wishlist/{archived}", null))).Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await ProblemAsync(await customer.PutAsync("/api/wishlist/999999", null))).Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await _api.Anonymous().GetAsync("/api/wishlist")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync("/api/wishlist")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "حساب إدارة بلا ملف عميل");
    }

    [Fact]
    public async Task دمج_قائمة_الزائر_يتجاهل_المكرّر_والمجهول_ومنتج_متجر_آخر()
    {
        var admin = await _api.AdminAsync();
        var kept = await _api.CreateProductAsync(admin);
        var local1 = await _api.CreateProductAsync(admin);
        var local2 = await _api.CreateProductAsync(admin);
        var other = _api.ForStore(await _factory.CreateStoreAsync());
        var foreign = await other.CreateProductAsync(await other.AdminAsync());
        var (customer, _) = await _api.NewCustomerAsync();
        (await customer.PutAsync($"/api/wishlist/{kept}", null)).EnsureSuccessStatusCode();

        var merged = await ReadAsync(await customer.PostAsJsonAsync("/api/wishlist/merge",
            new { productIds = new[] { local1, kept, foreign, 999999, local2, local1 } }));

        merged.Items.Select(i => i.Id).Should().Equal(local2, local1, kept);
        (await other.WithDbAsync(db => db.WishlistItems.CountAsync())).Should().Be(0, "لا صفّ يشير لمنتج متجر آخر");
        (await customer.PostAsJsonAsync("/api/wishlist/merge", new { productIds = new[] { 0 } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task محو_العميل_يحذف_مفضّلته()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin);
        var (customer, email) = await _api.NewCustomerAsync();
        (await customer.PutAsync($"/api/wishlist/{productId}", null)).EnsureSuccessStatusCode();
        var customerId = await _api.WithDbAsync(db => db.Customers.Where(c => c.Email == email).Select(c => c.Id).SingleAsync());

        (await customer.PostAsJsonAsync("/api/account/erase", new { password = "Customer-Pass-1" })).EnsureSuccessStatusCode();

        (await _api.WithDbAsync(db => db.WishlistItems.CountAsync(w => w.CustomerId == customerId))).Should().Be(0);
    }

    private static async Task<WishlistBody> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WishlistBody>(TestApi.Json))!;
    }

    private static async Task<WishlistBody> GetAsync(HttpClient customer) =>
        (await customer.GetFromJsonAsync<WishlistBody>("/api/wishlist", TestApi.Json))!;

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record TextBody(string Name);
    private sealed record ItemBody(int Id, string Name, Dictionary<string, TextBody> Translations, decimal Price, int StockQuantity);
    private sealed record WishlistBody(List<ItemBody> Items);
}
