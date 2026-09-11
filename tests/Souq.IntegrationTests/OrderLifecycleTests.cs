using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Enums;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الطلبات (المرحلة 9) عبر HTTP وSQL Server الحقيقيين: رقم متسلسل لكل متجر من 1001، الدفع من السلة واستهلاكها بعد الدفع
// لا قبله، إلغاء العميل قبل الدفع فقط مع تحرير الحجز، التتبّع العام بالرمز بلا ملاحظات، مرشّحات الإدارة ومن غيّر الحالة،
// والإجماليات المثبَّتة. العزل بين المتاجر في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class OrderLifecycleTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public OrderLifecycleTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task أرقام_الطلبات_متسلسلة_لكل_متجر_من_1001()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());
        var storeAdmin = await store.AdminAsync();
        var storeProduct = await store.CreateProductAsync(storeAdmin, price: 5m, stock: 10);
        var (storeCustomer, _) = await store.NewCustomerAsync();

        var first = await CreatedAsync(await store.PlaceOrderAsync(storeCustomer, storeProduct, 1));
        var second = await CreatedAsync(await store.PlaceOrderAsync(storeCustomer, storeProduct, 1));
        (first.OrderNumber, second.OrderNumber).Should().Be((1001, 1002));

        // عدّاد متجر لا يمسّ متجراً آخر: الطلب التالي في المتجر الافتراضي يلي آخر أرقامه هو.
        var (customer, _) = await _api.NewCustomerAsync();
        var product = await _api.CreateProductAsync(await _api.AdminAsync(), price: 5m, stock: 5);
        var last = await _api.WithDbAsync(db => db.Orders.MaxAsync(o => (int?)o.OrderNumber)) ?? 1000;
        (await CreatedAsync(await _api.PlaceOrderAsync(customer, product, 1))).OrderNumber.Should().Be(last + 1);
    }

    [Fact]
    public async Task الطلب_من_السلة_والسلة_تُستهلك_بعد_الدفع_لا_قبله()
    {
        var admin = await _api.AdminAsync();
        var first = await _api.CreateProductAsync(admin, price: 4m, stock: 10);
        var second = await _api.CreateProductAsync(admin, price: 6m, stock: 10);
        var (customer, _) = await _api.NewCustomerAsync();
        (await customer.PostAsJsonAsync("/api/basket/items", new { productId = first, quantity = 2 })).EnsureSuccessStatusCode();
        (await customer.PostAsJsonAsync("/api/basket/items", new { productId = second, quantity = 1 })).EnsureSuccessStatusCode();

        var order = await CreatedAsync(await customer.PostAsJsonAsync("/api/orders", new { shippingAddress = "عمّان — عنوان اختبار" }));
        order.TotalAmount.Should().Be(14m);
        (await BasketItemCountAsync(customer)).Should().Be(3, "السلة لا تُفرغ قبل الدفع — دفع فاشل يتركها كما هي");

        (await customer.PostAsync($"/api/orders/{order.OrderId}/confirm-payment", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BasketItemCountAsync(customer)).Should().Be(0);

        (await ProblemAsync(await customer.PostAsJsonAsync("/api/orders", new { shippingAddress = "عمّان" })))
            .Should().Be((HttpStatusCode.BadRequest, "BasketEmpty"));
    }

    [Fact]
    public async Task العميل_يلغي_قبل_الدفع_ويتحرّر_الحجز_ولا_يلغي_المدفوع_ولا_طلب_غيره()
    {
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 7m, stock: 3);
        var (customer, _) = await _api.NewCustomerAsync();
        var pending = await CreatedAsync(await _api.PlaceOrderAsync(customer, productId, 2));
        (await ReservedAsync(productId)).Should().Be(2);

        var (other, _) = await _api.NewCustomerAsync();
        (await other.PostAsync($"/api/orders/{pending.OrderId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await customer.PostAsJsonAsync($"/api/orders/{pending.OrderId}/cancel", new { reason = "غيّرت رأيي" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReservedAsync(productId)).Should().Be(0);
        var cancelled = await _api.WithDbAsync(db => db.Orders.Where(o => o.Id == pending.OrderId).SelectMany(o => o.StatusHistory)
            .OrderByDescending(h => h.Id).Select(h => new { h.Status, h.ChangedBy, h.Note }).FirstAsync());
        cancelled.Should().BeEquivalentTo(new { Status = OrderStatus.Cancelled, ChangedBy = OrderActorKind.Customer, Note = "غيّرت رأيي" });

        var paid = await CreatedAsync(await _api.PlaceOrderAsync(customer, productId, 1));
        (await customer.PostAsync($"/api/orders/{paid.OrderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        (await ProblemAsync(await customer.PostAsync($"/api/orders/{paid.OrderId}/cancel", null)))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidOrderOperation"));
    }

    [Fact]
    public async Task التتبّع_العام_بالرمز_بلا_ملاحظات_والمعرّف_لا_يفتح_شيئاً()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 9m, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        var order = await CreatedAsync(await _api.PlaceOrderAsync(customer, productId, 1));
        (await customer.PostAsync($"/api/orders/{order.OrderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{order.OrderId}/status", new { action = "Ship", note = "ملاحظة داخلية", trackingNumber = "TRK-9" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = await _api.WithDbAsync(db => db.Orders.Where(o => o.Id == order.OrderId).Select(o => o.TrackingToken).SingleAsync());

        var tracked = await _api.Anonymous().GetAsync($"/api/orders/track/{token}");
        tracked.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await tracked.Content.ReadAsStringAsync();
        body.Should().Contain("TRK-9").And.NotContain("ملاحظة داخلية").And.NotContain("changedBy").And.NotContain("عمّان");
        using (var json = JsonDocument.Parse(body))
            json.RootElement.GetProperty("orderNumber").GetInt32().Should().Be(order.OrderNumber);

        // المعرّف التسلسلي لم يعد رابط تتبّع (B8)، ورمز لا يطابق أو بشكل خاطئ = غير موجود.
        (await _api.Anonymous().GetAsync($"/api/orders/{order.OrderId}/tracking")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.Anonymous().GetAsync($"/api/orders/track/{new string('0', 32)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.Anonymous().GetAsync("/api/orders/track/short")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task الإدارة_تبحث_وتصفّي_وترى_من_غيّر_والعميل_لا_يرى_الملاحظات()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 11m, stock: 10);
        var (customer, email) = await _api.NewCustomerAsync();
        var shipped = await CreatedAsync(await _api.PlaceOrderAsync(customer, productId, 1));
        (await customer.PostAsync($"/api/orders/{shipped.OrderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{shipped.OrderId}/status", new { action = "Ship", note = "سلّم للمندوب" })).EnsureSuccessStatusCode();
        var cancelled = await CreatedAsync(await _api.PlaceOrderAsync(customer, productId, 1));
        (await customer.PostAsync($"/api/orders/{cancelled.OrderId}/cancel", null)).EnsureSuccessStatusCode();

        (await IdsAsync(admin, $"/api/orders?search={shipped.OrderNumber}")).Should().Equal(shipped.OrderId);
        (await IdsAsync(admin, $"/api/orders?search=%23{cancelled.OrderNumber}")).Should().Equal(cancelled.OrderId);
        (await IdsAsync(admin, $"/api/orders?search={Uri.EscapeDataString(email)}")).Should().BeEquivalentTo(new[] { shipped.OrderId, cancelled.OrderId });
        (await IdsAsync(admin, $"/api/orders?search={Uri.EscapeDataString(email)}&status=Cancelled")).Should().Equal(cancelled.OrderId);

        var asAdmin = await OrderAsync(admin, shipped.OrderId);
        asAdmin.AllowedActions.Should().Equal("Deliver");
        asAdmin.History.Select(h => h.ChangedBy).Should().Equal("Customer", "PaymentGateway", "Staff");
        (asAdmin.History.Last().Status, asAdmin.History.Last().Note).Should().Be(("Shipped", "سلّم للمندوب"));
        asAdmin.History.Last().ChangedByName.Should().NotBeNullOrWhiteSpace();

        var asCustomer = await OrderAsync(customer, shipped.OrderId);
        asCustomer.History.Should().OnlyContain(h => h.Note == null && h.ChangedBy == null && h.ChangedByName == null);
        (asCustomer.CanCancel, asCustomer.AllowedActions.Count, asCustomer.TrackingToken.Length).Should().Be((false, 0, 32));
    }

    [Fact]
    public async Task الإجماليات_مثبَّتة_لا_يغيّرها_تعديل_السعر()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 20m, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        var order = await CreatedAsync(await _api.PlaceOrderAsync(customer, productId, 2));

        var product = await _api.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => new { p.Slug, p.CategoryId }).SingleAsync());
        (await admin.PutAsJsonAsync($"/api/products/{productId}", TestApi.ProductUpdateBody(product.CategoryId, product.Slug, price: 99m)))
            .IsSuccessStatusCode.Should().BeTrue();

        var stored = await _api.WithDbAsync(db => db.Orders.Where(o => o.Id == order.OrderId)
            .Select(o => new { o.PlacedSubtotal, o.PlacedTotal, Placed = o.PlacedAt != null }).SingleAsync());
        stored.Should().BeEquivalentTo(new { PlacedSubtotal = 40m, PlacedTotal = 40m, Placed = true });
        (await OrderAsync(customer, order.OrderId)).TotalAmount.Should().Be(40m);
    }

    private Task<int> ReservedAsync(int productId) =>
        _api.WithDbAsync(db => db.InventoryItems.Where(i => i.ProductId == productId).Select(i => i.Reserved).SingleAsync());

    private static async Task<Created> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Created>(TestApi.Json))!;
    }

    private static async Task<OrderBody> OrderAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<OrderBody>($"/api/orders/{id}", TestApi.Json))!;

    private static async Task<List<int>> IdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>(url, TestApi.Json))!.Items.Select(i => i.Id).ToList();

    private static async Task<int> BasketItemCountAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<TestApi.BasketBody>("/api/basket", TestApi.Json))!.ItemCount;

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record Created(int OrderId, int OrderNumber, string Status, decimal TotalAmount);

    private sealed record OrderBody(
        int Id, int OrderNumber, string Status, decimal TotalAmount, string TrackingToken,
        List<HistoryBody> History, List<string> AllowedActions, bool CanCancel);

    private sealed record HistoryBody(string Status, DateTime ChangedAt, string? Note, string? ChangedBy, string? ChangedByName);
}
