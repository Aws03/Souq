using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Enums;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// استخدامات الكوبونات (المرحلة 10) عبر HTTP وSQL Server الحقيقيين: طلبات متزامنة على آخر استخدام ينجح منها واحد، حدّ
// العميل يُحتسب بالطلبات القائمة ويعود بالإلغاء (من العميل أو الإدارة)، الدفع يؤكّد، الكوبون الذي لم يبدأ يُرفض، والإدارة
// ترى الاستخدامات ولا تحذف كوبوناً مستخدماً. العزل بين المتاجر في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CouponRedemptionTests
{
    private readonly TestApi _api;

    public CouponRedemptionTests(SouqApiFactory factory) => _api = new TestApi(factory);

    [Fact]
    public async Task طلبات_متزامنة_على_آخر_استخدام_ينجح_واحد_فقط()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 20m, stock: 20);
        var (couponId, code) = await CreateCouponAsync(admin, maxUses: 1);
        var customers = new List<HttpClient>();
        for (var i = 0; i < 5; i++) customers.Add((await _api.NewCustomerAsync()).Client);

        var responses = await Task.WhenAll(customers.Select(c => OrderWithCouponAsync(c, productId, code)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            (await ProblemAsync(rejected)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidCoupon"));
        (await UsedAsync(couponId)).Should().Be(1);
        (await _api.WithDbAsync(db => db.CouponRedemptions.CountAsync(r => r.CouponId == couponId))).Should().Be(1);
        (await _api.WithDbAsync(db => db.Orders.CountAsync(o => o.CouponCode == code))).Should().Be(1, "الخاسرون لم يُنشأ لهم طلب");
    }

    [Fact]
    public async Task حدّ_العميل_يُحتسب_بالطلبات_القائمة_والإلغاء_يعيد_الاستخدام_والدفع_يؤكّده()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 20m, stock: 10);
        var (couponId, code) = await CreateCouponAsync(admin, maxUsesPerCustomer: 1);
        var (customer, _) = await _api.NewCustomerAsync();

        var first = await CreatedAsync(await OrderWithCouponAsync(customer, productId, code));
        (await ProblemAsync(await OrderWithCouponAsync(customer, productId, code)))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidCoupon"), "طلب غير مدفوع يحجز استخدامه");
        var quote = (await customer.GetFromJsonAsync<TestApi.BasketBody>($"/api/basket/quote?couponCode={code}", TestApi.Json))!;
        (quote.Coupon!.Applied, quote.Coupon.ErrorCode).Should().Be((false, "InvalidCoupon"));

        (await customer.PostAsync($"/api/orders/{first.OrderId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RedemptionStatusAsync(first.OrderId), await UsedAsync(couponId)).Should().Be((CouponRedemptionStatus.Released, 0));

        var second = await CreatedAsync(await OrderWithCouponAsync(customer, productId, code));
        (await customer.PostAsync($"/api/orders/{second.OrderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        (await RedemptionStatusAsync(second.OrderId), await UsedAsync(couponId)).Should().Be((CouponRedemptionStatus.Confirmed, 1));

        // إلغاء الإدارة لطلب مدفوع يعيد الاستخدام أيضاً.
        (await admin.PutAsJsonAsync($"/api/orders/{second.OrderId}/status", new { action = "Cancel" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RedemptionStatusAsync(second.OrderId), await UsedAsync(couponId)).Should().Be((CouponRedemptionStatus.Released, 0));
    }

    [Fact]
    public async Task كوبون_لم_يبدأ_يُرفض_والإدارة_ترى_الاستخدامات_ولا_تحذف_المستخدم()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 30m, stock: 10);
        var (customer, _) = await _api.NewCustomerAsync();
        var (_, future) = await CreateCouponAsync(admin, startsAt: DateTime.UtcNow.AddDays(3));
        (await ProblemAsync(await OrderWithCouponAsync(customer, productId, future)))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidCoupon"));

        var (couponId, code) = await CreateCouponAsync(admin);
        var order = await CreatedAsync(await OrderWithCouponAsync(customer, productId, code));
        var page = (await admin.GetFromJsonAsync<TestApi.PageBody<RedemptionBody>>($"/api/coupons/{couponId}/redemptions", TestApi.Json))!;
        var redemption = page.Items.Should().ContainSingle().Subject;
        (redemption.OrderId, redemption.OrderNumber, redemption.Discount, redemption.Status)
            .Should().Be((order.OrderId, order.OrderNumber, 3m, "Reserved"));
        redemption.CustomerName.Should().NotBeNullOrWhiteSpace();

        (await ProblemAsync(await admin.DeleteAsync($"/api/coupons/{couponId}"))).Should().Be((HttpStatusCode.Conflict, "CouponInUse"));
        var (unusedId, _) = await CreateCouponAsync(admin);
        (await admin.DeleteAsync($"/api/coupons/{unusedId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<(int Id, string Code)> CreateCouponAsync(
        HttpClient admin, int? maxUses = null, int? maxUsesPerCustomer = null, DateTime? startsAt = null)
    {
        var code = $"RD{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var response = await admin.PostAsJsonAsync("/api/coupons",
            new { code, type = "Percentage", value = 10m, maxUses, maxUsesPerCustomer, startsAt });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await response.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id, code);
    }

    private static Task<HttpResponseMessage> OrderWithCouponAsync(HttpClient customer, int productId, string code) =>
        customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = new[] { new { productId, quantity = 1 } },
            couponCode = code,
        });

    private Task<int> UsedAsync(int couponId) =>
        _api.WithDbAsync(db => db.Coupons.Where(c => c.Id == couponId).Select(c => c.UsedCount).SingleAsync());

    private Task<CouponRedemptionStatus> RedemptionStatusAsync(int orderId) =>
        _api.WithDbAsync(db => db.CouponRedemptions.Where(r => r.OrderId == orderId).Select(r => r.Status).SingleAsync());

    private static async Task<Created> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Created>(TestApi.Json))!;
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record Created(int OrderId, int OrderNumber, string Status, decimal TotalAmount);

    private sealed record RedemptionBody(
        int OrderId, int OrderNumber, int CustomerId, string? CustomerName, decimal Discount, string Currency, string Status,
        DateTime CreatedAt);
}
