using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// إيراد المنصّة عبر المتاجر (C11) — أوّل رقم مالٍ يعبر من متجر إلى شاشة المنصّة.
//
// ثلاثة أسئلة لا تُجاب بقراءة الشيفرة: هل الرقم هو نفسه الذي يراه التاجر في لوحته؟ وهل تُجمَع
// عملتان في رقم واحد بلا وحدة؟ ومن يُسمح له أصلاً؟
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class PlatformRevenueTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public PlatformRevenueTests(SouqApiFactory factory)
    {
        _factory = factory; _api = new TestApi(factory);
    }

    private sealed record StoreRevenue(int TenantId, string Slug, string Name, string Currency, decimal Revenue, int Orders);

    private static async Task<JsonElement> RevenueAsync(HttpClient owner, int days = 30)
    {
        var response = await owner.GetAsync($"/api/platform/revenue?days={days}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestApi.Json);
    }

    private static StoreRevenue? StoreIn(JsonElement revenue, string slug) =>
        revenue.GetProperty("byStore").EnumerateArray()
            .Where(s => s.GetProperty("slug").GetString() == slug)
            .Select(s => new StoreRevenue(
                s.GetProperty("tenantId").GetInt32(), s.GetProperty("slug").GetString()!,
                s.GetProperty("name").GetString()!, s.GetProperty("currency").GetString()!,
                s.GetProperty("revenue").GetDecimal(), s.GetProperty("orders").GetInt32()))
            .FirstOrDefault();

    private async Task<decimal> SellAsync(TestStore store, decimal price, int quantity)
    {
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: price, stock: 100);
        var (customer, _) = await api.NewCustomerAsync();
        var created = await api.PlaceOrderAsync(customer, productId, quantity);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString();
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        return price * quantity;
    }

    // ========================================================================
    // **لا تُجمَع عملتان في رقم واحد.** متجر بالدينار وآخر بالين: جمعهما يُنتج عدداً بلا وحدة،
    // وهو بالضبط نوع "الرؤية" التي تبدو ذات معنى وليس لها معنى. المجاميع لكل عملة.
    // ========================================================================
    [Fact]
    public async Task الإيراد_يُجمَّع_لكل_عملة_ولا_يُخلَط_في_رقم_واحد()
    {
        var jod = await _factory.CreateStoreAsync(currency: "JOD");
        var jpy = await _factory.CreateStoreAsync(currency: "JPY");
        var owner = await _api.PlatformOwnerAsync();

        var before = await RevenueAsync(owner);
        decimal TotalFor(JsonElement r, string currency) => r.GetProperty("totals").EnumerateArray()
            .Where(t => t.GetProperty("currency").GetString() == currency)
            .Select(t => t.GetProperty("revenue").GetDecimal()).FirstOrDefault();

        var soldJod = await SellAsync(jod, 30m, 2);     // 60 دينار
        var soldJpy = await SellAsync(jpy, 500m, 3);    // 1500 ين

        var after = await RevenueAsync(owner);

        TotalFor(after, "JOD").Should().Be(TotalFor(before, "JOD") + soldJod);
        TotalFor(after, "JPY").Should().Be(TotalFor(before, "JPY") + soldJpy);

        StoreIn(after, jod.Tenant.Slug)!.Currency.Should().Be("JOD");
        StoreIn(after, jpy.Tenant.Slug)!.Currency.Should().Be("JPY");
    }

    // ========================================================================
    // رقم المنصّة هو رقم التاجر نفسه. لو اختلف التعريفان لصار لكل طرف حقيقةٌ صحيحة بتعريفه،
    // ولا أحد يعرف أيّهما — وهو أسوأ من رقم خاطئ واحد.
    // ========================================================================
    [Fact]
    public async Task رقم_المنصّة_لمتجر_يطابق_ما_يراه_التاجر_في_لوحته()
    {
        var store = await _factory.CreateStoreAsync();
        var owner = await _api.PlatformOwnerAsync();
        await SellAsync(store, 25m, 4);   // 100

        var admin = await _api.ForStore(store).AdminAsync();
        var dashboard = await (await admin.GetAsync("/api/admin/reports/dashboard?range=Last30Days"))
            .Content.ReadFromJsonAsync<JsonElement>(TestApi.Json);
        var merchantRevenue = dashboard.GetProperty("current").GetProperty("revenue").GetDecimal();

        var platform = StoreIn(await RevenueAsync(owner), store.Tenant.Slug)!;

        platform.Revenue.Should().Be(merchantRevenue);
        platform.Orders.Should().Be(dashboard.GetProperty("current").GetProperty("orders").GetInt32());
    }

    [Fact]
    public async Task طلب_لم_يُدفع_لا_يدخل_إيراد_المنصّة()
    {
        var store = await _factory.CreateStoreAsync();
        var owner = await _api.PlatformOwnerAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 40m, stock: 10);

        var before = StoreIn(await RevenueAsync(owner), store.Tenant.Slug);

        var (customer, _) = await api.NewCustomerAsync();
        (await api.PlaceOrderAsync(customer, productId, 1)).StatusCode.Should().Be(HttpStatusCode.Created);

        StoreIn(await RevenueAsync(owner), store.Tenant.Slug)?.Revenue
            .Should().Be(before?.Revenue, "الطلب غير المدفوع ليس إيراداً — التعريف نفسه في الطرفين");
    }

    // ========================================================================
    // النقطة في منطقة المنصّة: غير موجودة على مضيف متجر، ومغلقة على من لا يملك الصلاحية.
    // ========================================================================
    [Fact]
    public async Task إيراد_المنصّة_محصور_في_مضيفها_وصلاحيتها()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);

        (await storeApi.Anonymous().GetAsync("/api/platform/revenue"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "على مضيف متجر: لا نكشف وجودها أصلاً");

        (await _api.Client(SouqApiFactory.PlatformHost).GetAsync("/api/platform/revenue"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "على مضيف المنصّة بلا توكن: الوجود معلوم والهوية مفقودة");

        // ومدير المتجر لا يصل إليها ولو حمل توكناً صالحاً: توكن متجر على مضيف المنصّة.
        var storeAdminToken = await storeApi.AdminTokenAsync();
        (await _api.Authorized(storeAdminToken, SouqApiFactory.PlatformHost).GetAsync("/api/platform/revenue"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task مدّة_خارج_الحدّ_تُرفض_بـ400_لا_تُمسح_الجدول()
    {
        var owner = await _api.PlatformOwnerAsync();

        // بلا سقف يصير `?days=100000` مسحاً لجدول الطلبات كلّه.
        (await owner.GetAsync("/api/platform/revenue?days=100000")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await owner.GetAsync("/api/platform/revenue?days=0")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }
}
