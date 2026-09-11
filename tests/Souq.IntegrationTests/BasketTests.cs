using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Features.Baskets;
using Souq.Domain.Entities;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// السلة (المرحلة 8) عبر HTTP وSQL Server الحقيقيين: سلة زائر بملف تعريف ارتباط، تسعير حيّ من الكتالوج، حدود المتاح بلا
// حجز، الدمج عند الدخول، تساوي إجمالي السلة وإجمالي الطلب، الانتهاء، والمحو. العزل بين المتاجر في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class BasketTests
{
    private const string CustomerPassword = "Customer-Pass-1";

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public BasketTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task سلة_الزائر_بملف_تعريف_ارتباط_محمي_وتسعير_حيّ()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 12.5m, stock: 5);
        var guest = _api.SecureClient();

        var added = await guest.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 2 });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var cookie = added.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith($"{TestApi.GuestBasketCookie}=", StringComparison.Ordinal));
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/api/basket");

        var basket = await BasketAsync(guest);
        var line = basket.Lines.Should().ContainSingle().Subject;
        (line.ProductId, line.Quantity, line.UnitPrice, line.LineTotal, line.Sellable, line.Available)
            .Should().Be((productId, 2, 12.5m, 25m, true, 5));
        (basket.Subtotal, basket.Shipping, basket.Tax, basket.Total, basket.ReadyForCheckout).Should().Be((25m, 0m, 0m, 25m, true));

        // السعر من الكتالوج الحيّ: تعديله يظهر في السلة فوراً — السلة لا تجمّد سعراً.
        var product = await _api.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => new { p.Slug, p.CategoryId }).SingleAsync());
        (await admin.PutAsJsonAsync($"/api/products/{productId}", TestApi.ProductUpdateBody(product.CategoryId, product.Slug, price: 10m)))
            .IsSuccessStatusCode.Should().BeTrue();
        (await BasketAsync(guest)).Total.Should().Be(20m);

        (await ReadAsync(await guest.PutAsJsonAsync($"/api/basket/items/{productId}", new { quantity = 3 }))).Lines.Single().Quantity.Should().Be(3);
        (await ReadAsync(await guest.DeleteAsync($"/api/basket/items/{productId}"))).Lines.Should().BeEmpty();

        (await guest.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })).EnsureSuccessStatusCode();
        (await guest.DeleteAsync("/api/basket")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await BasketAsync(guest)).ItemCount.Should().Be(0);

        // بلا ملف تعريف: سلة فارغة، والقراءة لا تُنشئ سلة ولا رمزاً.
        var stranger = await _api.SecureClient().GetAsync("/api/basket");
        (await ReadAsync(stranger)).Lines.Should().BeEmpty();
        stranger.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task الإضافة_لا_تتجاوز_المتاح_ولا_تحجز_والمؤرشف_خارج_المجموع()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 5m, stock: 2);
        var guest = _api.SecureClient();

        (await ProblemAsync(await guest.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 3 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));
        (await guest.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 2 })).EnsureSuccessStatusCode();
        (await ProblemAsync(await guest.PutAsJsonAsync($"/api/basket/items/{productId}", new { quantity = 3 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));
        (await ProblemAsync(await guest.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 0 })))
            .Should().Be((HttpStatusCode.BadRequest, "ValidationFailed"));

        // السلة لا تحجز (الدفع وحده يحجز، ADR-0026): المحجوز صفر رغم وجود الصنف في السلة.
        (await _api.WithDbAsync(db => db.InventoryItems.Where(i => i.ProductId == productId).Select(i => i.Reserved).SingleAsync()))
            .Should().Be(0);

        // أرشفة بعد الإضافة: السطر يبقى معروضاً غير قابل للبيع وخارج المجموع، والدفع غير جاهز؛ ولا يُضاف مؤرشف جديداً.
        (await admin.DeleteAsync($"/api/products/{productId}")).IsSuccessStatusCode.Should().BeTrue();
        var basket = await BasketAsync(guest);
        basket.Lines.Single().Sellable.Should().BeFalse();
        (basket.Subtotal, basket.Total, basket.ReadyForCheckout).Should().Be((0m, 0m, false));
        (await ProblemAsync(await _api.SecureClient().PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
    }

    [Fact]
    public async Task الدخول_يدمج_سلة_الزائر_ويمسح_رمزها_والسلة_تتبع_العميل_لا_الجهاز()
    {
        var admin = await _api.AdminAsync();
        var shared = await _api.CreateProductAsync(admin, price: 4m, stock: 10);
        var guestOnly = await _api.CreateProductAsync(admin, price: 6m, stock: 10);
        var (customer, email) = await _api.NewCustomerAsync();
        (await customer.PostAsJsonAsync("/api/basket/items", new { productId = shared, quantity = 2 })).EnsureSuccessStatusCode();

        // المتصفّح نفسه: زائراً يضيف، ثم يدخل فيحمل الرمز والجلسة معاً.
        var browser = _api.SecureClient();
        var first = await browser.PostAsJsonAsync("/api/basket/items", new { productId = shared, quantity = 1 });
        var guestToken = TestApi.GuestBasketToken(first);
        (await browser.PostAsJsonAsync("/api/basket/items", new { productId = guestOnly, quantity = 1 })).EnsureSuccessStatusCode();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _api.TokenAsync(email, CustomerPassword));

        var merged = await browser.GetAsync("/api/basket");
        (await ReadAsync(merged)).Lines.Select(l => (l.ProductId, l.Quantity))
            .Should().BeEquivalentTo(new[] { (shared, 3), (guestOnly, 1) });
        merged.Headers.GetValues("Set-Cookie").Should().Contain(h => h.StartsWith($"{TestApi.GuestBasketCookie}=;", StringComparison.Ordinal));

        // السلة نفسها من جهاز آخر للعميل (بالجلسة لا بالرمز) — تنجو من التحديث وتبديل الجهاز (C12).
        (await BasketAsync(customer)).ItemCount.Should().Be(4);

        // رمز الزائر القديم لم يعد يطابق شيئاً.
        var stale = _api.SecureClient(handleCookies: false);
        stale.DefaultRequestHeaders.Add("Cookie", $"{TestApi.GuestBasketCookie}={guestToken}");
        (await BasketAsync(stale)).Lines.Should().BeEmpty();
        var hash = GuestBasketTokens.Hash(guestToken);
        (await _api.WithDbAsync(db => db.Baskets.CountAsync(b => b.GuestTokenHash == hash))).Should().Be(0);
    }

    [Fact]
    public async Task إجمالي_السلة_يساوي_إجمالي_الطلب_بالكوبون_نفسه()
    {
        var admin = await _api.AdminAsync();
        var first = await _api.CreateProductAsync(admin, price: 12.35m, stock: 10);
        var second = await _api.CreateProductAsync(admin, price: 7.1m, stock: 10);
        var code = $"BSK{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        (await admin.PostAsJsonAsync("/api/coupons", new { code, type = "Percentage", value = 15m })).StatusCode.Should().Be(HttpStatusCode.Created);
        var (customer, _) = await _api.NewCustomerAsync();
        (await customer.PostAsJsonAsync("/api/basket/items", new { productId = first, quantity = 3 })).EnsureSuccessStatusCode();
        (await customer.PostAsJsonAsync("/api/basket/items", new { productId = second, quantity = 2 })).EnsureSuccessStatusCode();

        var quote = await ReadAsync(await customer.GetAsync($"/api/basket/quote?couponCode={code}"));
        (quote.Coupon!.Applied, quote.Discount > 0, quote.ReadyForCheckout).Should().Be((true, true, true));

        var placed = await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = quote.Lines.Select(l => new { l.ProductId, l.Quantity }),
            couponCode = code,
        });
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var order = (await placed.Content.ReadFromJsonAsync<OrderTotals>(TestApi.Json))!;
        (order.Subtotal, order.DiscountAmount, order.TotalAmount).Should().Be((quote.Subtotal, quote.Discount, quote.Total));

        // كوبون غير موجود: السلة معروضة والرفض نتيجة فيها، لا خطأ — والدفع غير جاهز.
        var rejected = await ReadAsync(await customer.GetAsync("/api/basket/quote?couponCode=NO-SUCH-CODE"));
        (rejected.Coupon!.Applied, rejected.Coupon.ErrorCode, rejected.Total, rejected.ReadyForCheckout)
            .Should().Be((false, "CouponNotFound", quote.Subtotal, false));
    }

    [Fact]
    public async Task المنتهية_لا_تُستعمل_والمنسّق_يحذفها_والمحو_يحذف_سلة_العميل()
    {
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 3m, stock: 5);

        // سلة زائر منتهية: قراءتها تعطي سلة فارغة وتحذفها.
        var guest = _api.SecureClient();
        var hash = GuestBasketTokens.Hash(TestApi.GuestBasketToken(
            await guest.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })));
        await ExpireAsync(b => b.GuestTokenHash == hash);
        (await BasketAsync(guest)).Lines.Should().BeEmpty();
        (await _api.WithDbAsync(db => db.Baskets.CountAsync(b => b.GuestTokenHash == hash))).Should().Be(0);

        // سلة عميل منتهية لم يطلبها أحد: يحذفها المنسّق بأسطرها.
        var (idle, idleEmail) = await _api.NewCustomerAsync();
        (await idle.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })).EnsureSuccessStatusCode();
        var idleId = await CustomerIdAsync(idleEmail);
        await ExpireAsync(b => b.CustomerId == idleId);
        await using (var scope = await _factory.TenantScopeAsync())
            (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new PurgeExpiredBasketsCommand()))
                .Should().BeGreaterThanOrEqualTo(1);
        (await _api.WithDbAsync(db => db.Baskets.AnyAsync(b => b.CustomerId == idleId))).Should().BeFalse();
        (await _api.WithDbAsync(db => db.Set<BasketLine>().AnyAsync(l => l.ProductId == productId))).Should().BeFalse();

        // محو الحساب يحذف سلة العميل (نيّة شراء لا سجلّ).
        var (leaving, leavingEmail) = await _api.NewCustomerAsync();
        (await leaving.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })).EnsureSuccessStatusCode();
        var leavingId = await CustomerIdAsync(leavingEmail);
        (await leaving.PostAsJsonAsync("/api/account/erase", new { password = CustomerPassword })).IsSuccessStatusCode.Should().BeTrue();
        (await _api.WithDbAsync(db => db.Baskets.AnyAsync(b => b.CustomerId == leavingId))).Should().BeFalse();
    }

    private async Task ExpireAsync(System.Linq.Expressions.Expression<Func<Basket, bool>> which)
    {
        await using var scope = await _factory.TenantScopeAsync();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Baskets.Where(which)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    private Task<int> CustomerIdAsync(string email) =>
        _api.WithDbAsync(db => db.Customers.Where(c => c.Email == email).Select(c => c.Id).SingleAsync());

    private static async Task<TestApi.BasketBody> BasketAsync(HttpClient client) => await ReadAsync(await client.GetAsync("/api/basket"));

    private static async Task<TestApi.BasketBody> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.BasketBody>(TestApi.Json))!;
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record OrderTotals(decimal Subtotal, decimal? DiscountAmount, decimal TotalAmount);
}
