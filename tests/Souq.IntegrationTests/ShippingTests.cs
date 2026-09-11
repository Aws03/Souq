using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الشحن (المرحلة 12) عبر HTTP وSQL Server الحقيقيين: الإدارة تعرّف طرقاً، التسعير يعرض ما يخدم دولة العنوان بسعره (مجاني فوق
// الحدّ)، الدفع يلزمه اختيار طريقة تخدم دولة عنوان الدفتر ويدخل الشحن الإجمالي ونيّة الدفع، ولقطة الطريقة ورابط تتبّع ناقلها
// على الطلب. كل اختبار في متجر جديد: طريقة مفعّلة في المتجر الافتراضي تلزم كل دفع في الاختبارات الأخرى باختيار شحن.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ShippingTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public ShippingTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task التسعير_يعرض_ما_يخدم_الدولة_والدفع_يلزمه_اختيار_ويدخل_الشحن_الإجمالي_ورابط_التتبّع()
    {
        var api = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 20m, stock: 10);
        var standard = await CreateMethodAsync(admin, new
        {
            name = "عادي", price = 2.5m, freeOverAmount = 100m, minDays = 2, maxDays = 4, carrier = "Aramex",
            trackingUrlTemplate = "https://track.example/{number}", countries = new[] { "JO" },
        });
        var express = await CreateMethodAsync(admin, new { name = "سريع", price = 6m, minDays = 1, maxDays = 1, sortOrder = 1 });
        await CreateMethodAsync(admin, new { name = "معطّلة", price = 1m, isActive = false });

        var (customer, _) = await api.NewCustomerAsync();
        (await customer.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 2 })).EnsureSuccessStatusCode();
        var jordan = await AddAddressAsync(customer, "JO");
        var egypt = await AddAddressAsync(customer, "EG");

        var quoteJo = (await customer.GetFromJsonAsync<QuoteBody>($"/api/basket/quote?country=JO&shippingMethodId={standard}", TestApi.Json))!;
        quoteJo.ShippingMethods!.Options.Select(o => (o.MethodId, o.Cost)).Should().Equal((standard, 2.5m), (express, 6m));
        (quoteJo.Shipping, quoteJo.Total, quoteJo.ShippingMethods.SelectedMethodId).Should().Be((2.5m, 42.5m, (int?)standard));
        var quoteEg = (await customer.GetFromJsonAsync<QuoteBody>("/api/basket/quote?country=EG", TestApi.Json))!;
        quoteEg.ShippingMethods!.Options.Select(o => o.MethodId).Should().Equal(express);
        (quoteEg.ShippingMethods.ErrorCode, quoteEg.ShippingMethods.Required).Should().Be(("ShippingMethodRequired", true));

        // الدولة من عنوان الدفتر على الخادم: طريقة الأردن لا تُختار لعنوان في مصر.
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/orders", new { shippingAddressId = jordan })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "ShippingMethodRequired"));
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/orders", new { shippingAddressId = egypt, shippingMethodId = standard })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "ShippingMethodUnavailable"));

        var created = await customer.PostAsJsonAsync("/api/orders", new { shippingAddressId = jordan, shippingMethodId = standard });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var order = (await created.Content.ReadFromJsonAsync<CreatedBody>(TestApi.Json))!;
        (order.TotalAmount, order.ShippingCost).Should().Be((42.5m, 2.5m));
        (await api.WithDbAsync(db => db.Payments.Where(p => p.OrderId == order.OrderId).Select(p => p.Amount.Amount).SingleAsync()))
            .Should().Be(42.5m, "نيّة الدفع بالإجمالي شاملاً الشحن");

        (await customer.PostAsync($"/api/orders/{order.OrderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{order.OrderId}/status", new { action = "Ship", trackingNumber = "JO 123" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = (await customer.GetFromJsonAsync<OrderBody>($"/api/orders/{order.OrderId}", TestApi.Json))!;
        (detail.ShippingMethod, detail.ShippingCost, detail.ShippingMinDays, detail.ShippingMaxDays, detail.ShippingCountry)
            .Should().Be(("عادي", 2.5m, (int?)2, (int?)4, "JO"));
        (detail.ShippingCarrier, detail.TrackingUrl, detail.TotalAmount).Should().Be(("Aramex", "https://track.example/JO%20123", 42.5m));
        var token = await api.WithDbAsync(db => db.Orders.Where(o => o.Id == order.OrderId).Select(o => o.TrackingToken).SingleAsync());
        (await api.Anonymous().GetFromJsonAsync<TrackingBody>($"/api/orders/track/{token}", TestApi.Json))!
            .TrackingUrl.Should().Be("https://track.example/JO%20123");
    }

    [Fact]
    public async Task الشحن_مجاني_حين_يبلغ_الإجمالي_الحدّ_ومتجر_بلا_طرق_يطلب_بلا_اختيار()
    {
        var api = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 30m, stock: 10);
        var method = await CreateMethodAsync(admin, new { name = "عادي", price = 3m, freeOverAmount = 50m });
        var (customer, _) = await api.NewCustomerAsync();

        (await customer.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })).EnsureSuccessStatusCode();
        var belowThreshold = (await customer.GetFromJsonAsync<QuoteBody>($"/api/basket/quote?shippingMethodId={method}", TestApi.Json))!;
        (await customer.PutAsJsonAsync($"/api/basket/items/{productId}", new { quantity = 2 })).EnsureSuccessStatusCode();
        var overThreshold = (await customer.GetFromJsonAsync<QuoteBody>($"/api/basket/quote?shippingMethodId={method}", TestApi.Json))!;

        (belowThreshold.Shipping, belowThreshold.Total).Should().Be((3m, 33m));
        (overThreshold.Shipping, overThreshold.Total).Should().Be((0m, 60m));

        // متجر لم يضبط الشحن: الدفع كما قبل المرحلة 12 — بلا طريقة، بلا تكلفة.
        var plain = _api.ForStore(await _factory.CreateStoreAsync());
        var plainProduct = await plain.CreateProductAsync(await plain.AdminAsync(), price: 10m, stock: 3);
        var (plainCustomer, _) = await plain.NewCustomerAsync();
        var placed = await plain.PlaceOrderAsync(plainCustomer, plainProduct, 1);
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        (await placed.Content.ReadFromJsonAsync<CreatedBody>(TestApi.Json))!.Should().BeEquivalentTo(new { TotalAmount = 10m, ShippingCost = 0m },
            o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task الإدارة_تعدّل_وتحذف_والقواعد_تُرفض_برمزها_والعميل_لا_يصل()
    {
        var api = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await api.AdminAsync();
        var id = await CreateMethodAsync(admin, new { name = "عادي", price = 2m });

        (await admin.PutAsJsonAsync($"/api/admin/shipping-methods/{id}",
            new { name = "محدَّث", price = 4.5m, countries = new[] { "jo", "sa" }, isActive = false, sortOrder = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var listed = (await admin.GetFromJsonAsync<List<MethodBody>>("/api/admin/shipping-methods", TestApi.Json))!.Single();
        (listed.Name, listed.Price, listed.Currency, listed.IsActive, string.Join(",", listed.Countries))
            .Should().Be(("محدَّث", 4.5m, "JOD", false, "JO,SA"));

        (await ProblemAsync(await admin.PostAsJsonAsync("/api/admin/shipping-methods",
            new { name = "رابط", price = 1m, trackingUrlTemplate = "http://insecure.example/{number}" })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidShippingMethod"));
        (await ProblemAsync(await admin.PostAsJsonAsync("/api/admin/shipping-methods", new { name = "دولة", price = 1m, countries = new[] { "JOR" } })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidShippingMethod"));

        var (customer, _) = await api.NewCustomerAsync();
        (await customer.GetAsync("/api/admin/shipping-methods")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.DeleteAsync($"/api/admin/shipping-methods/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<MethodBody>>("/api/admin/shipping-methods", TestApi.Json))!.Should().BeEmpty();
    }

    private static async Task<int> CreateMethodAsync(HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/shipping-methods", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
    }

    private static async Task<int> AddAddressAsync(HttpClient customer, string country)
    {
        var response = await customer.PostAsJsonAsync("/api/account/addresses", new
        {
            address = new { recipientName = "عميل", phone = "0790000000", country, city = "مدينة", line1 = "شارع 1" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record QuoteBody(decimal Subtotal, decimal Shipping, decimal Total, ShippingBody? ShippingMethods);
    private sealed record ShippingBody(List<OptionBody> Options, int? SelectedMethodId, bool Required, string? ErrorCode);
    private sealed record OptionBody(int MethodId, string Name, decimal Cost, int? MinDays, int? MaxDays);
    private sealed record CreatedBody(int OrderId, decimal TotalAmount, decimal ShippingCost);
    private sealed record OrderBody(
        decimal TotalAmount, string? ShippingMethod, decimal ShippingCost, int? ShippingMinDays, int? ShippingMaxDays,
        string? ShippingCountry, string? ShippingCarrier, string? TrackingUrl);
    private sealed record TrackingBody(string? TrackingUrl);
    private sealed record MethodBody(int Id, string Name, decimal Price, string Currency, List<string> Countries, bool IsActive);
}
