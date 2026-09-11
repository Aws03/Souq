using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Enums;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// المخزون والطلبات على SQL Server الحقيقي: التزامن (rowversion)، إعادة المخزون عند
// الإلغاء مع أثر في السجلّ، compare-and-set لتعديل الإدارة، ودقّة الدينار بثلاث خانات.
[Collection(IntegrationCollection.Name)]
public class InventoryAndOrderTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public InventoryAndOrderTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task شراءان_متزامنان_لآخر_وحدة_الثاني_يُرفض_بدل_البيع_الزائد()
    {
        // Phase 0 C1: نسختان في الذاكرة رأتا مخزون 1 وأنقصتا معاً — بلا rowversion ينجح
        // الحفظان فتُباع الوحدة الأخيرة مرتين. الآن يرفض المحرّك الحفظ الثاني.
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 1);
        await using var firstScope = await _factory.TenantScopeAsync();
        await using var secondScope = await _factory.TenantScopeAsync();
        var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstCopy = await first.Products.SingleAsync(p => p.Id == productId);
        var secondCopy = await second.Products.SingleAsync(p => p.Id == productId);
        firstCopy.DecreaseStock(1);
        secondCopy.DecreaseStock(1);

        await first.SaveChangesAsync();
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await StockOf(productId)).Should().Be(0);
    }

    [Fact]
    public async Task طلبات_متوازية_عبر_الـ_API_لا_تبيع_أكثر_من_المخزون_أبداً()
    {
        const int initialStock = 3;
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: initialStock);
        var customers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _api.NewCustomerAsync()));

        var responses = await Task.WhenAll(customers.Select(c => _api.PlaceOrderAsync(c.Client, productId, 1)));

        var sold = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        // 201 بيع، 409 خسر سباق rowversion، 422 رأى المخزون نافداً قبل الشراء (ADR-0017).
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created
                                            || r.StatusCode == HttpStatusCode.Conflict
                                            || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        sold.Should().BeInRange(1, initialStock);
        var finalStock = await StockOf(productId);
        finalStock.Should().Be(initialStock - sold).And.BeGreaterThanOrEqualTo(0);
        (await LedgerOf(productId)).Count(m => m.Type == StockMovementType.Sale).Should().Be(sold);
    }

    [Fact]
    public async Task إلغاء_الإدارة_يعيد_المخزون_ويسجّل_Cancellation_ولا_يتكرّر()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        var created = await _api.PlaceOrderAsync(customer, productId, 2);
        var orderId = (await created.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await StockOf(productId)).Should().Be(3);

        var cancel = await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Cancel" });
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StockOf(productId)).Should().Be(5);                                         // C2
        (await LedgerOf(productId)).Should().Contain(m => m.Type == StockMovementType.Cancellation && m.QuantityChange == 2);

        var again = await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Cancel" });
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);                  // C10 — قاعدة عمل
        (await again.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("InvalidOrderOperation");
        (await StockOf(productId)).Should().Be(5);                                         // لا إعادة مزدوجة
    }

    [Fact]
    public async Task تعديل_مخزون_من_نموذج_قديم_يُرفض_بـ_409_وتعديل_الاسم_وحده_لا_يمسّ_المخزون()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 5);
        var categoryId = (await admin.GetFromJsonAsync<List<TestApi.IdBody>>("/api/categories", TestApi.Json))!.First().Id;
        var (customer, _) = await _api.NewCustomerAsync();
        (await _api.PlaceOrderAsync(customer, productId, 1)).StatusCode.Should().Be(HttpStatusCode.Created); // 5 ⇒ 4

        // المدير فتح النموذج حين كان المخزون 5 ويحفظ 10 (Phase 0 C4).
        var stale = await admin.PutAsJsonAsync($"/api/products/{productId}", new
        {
            nameAr = "اسم", description = "وصف", price = 10m, stockQuantity = 10, expectedStockQuantity = 5,
            imageUrl = "placeholder", categoryId,
        });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StockOf(productId)).Should().Be(4);

        var renameOnly = await admin.PutAsJsonAsync($"/api/products/{productId}", new
        {
            nameAr = "اسم معدّل", description = "وصف", price = 10m, imageUrl = "placeholder", categoryId,
        });
        renameOnly.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StockOf(productId)).Should().Be(4);
    }

    [Fact]
    public async Task الدينار_بثلاث_خانات_يُخزَّن_ويُحسَب_بلا_تقريب_صامت()
    {
        // Phase 0 C5: decimal(18,2) كان يقرّب 12.345 إلى 12.35، والخصم المئوي يختلف بين
        // الذاكرة (المُرسَل للبوّابة) والقاعدة (المعروض على الطلب).
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 12.345m, stock: 5);
        var code = $"IT15{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        (await admin.PostAsJsonAsync("/api/coupons", new { code, type = "Percentage", value = 15 }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var product = await _api.Anonymous().GetFromJsonAsync<PriceBody>($"/api/products/{productId}", TestApi.Json);
        product!.Price.Should().Be(12.345m);

        var (customer, _) = await _api.NewCustomerAsync();
        var response = await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان", items = new[] { new { productId, quantity = 1 } }, couponCode = code,
        });
        var order = await response.Content.ReadFromJsonAsync<OrderTotalsBody>(TestApi.Json);

        order!.Subtotal.Should().Be(12.345m);
        order.DiscountAmount.Should().Be(1.852m);   // 15% = 1.85175 ⇒ تقريب تجاري للفلس
        order.TotalAmount.Should().Be(10.493m);
        var storedDiscount = await _api.WithDbAsync(db =>
            db.Orders.Where(o => o.Id == order.OrderId).Select(o => o.DiscountAmount!.Amount).SingleAsync());
        storedDiscount.Should().Be(1.852m);
    }

    [Fact]
    public async Task سعر_بخانات_أكثر_من_العملة_يُرفض_بـ_422_لا_500()
    {
        var admin = await _api.AdminAsync();
        var categoryId = (await admin.GetFromJsonAsync<List<TestApi.IdBody>>("/api/categories", TestApi.Json))!.First().Id;

        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            nameAr = "دقّة زائدة", description = "وصف", price = 12.3456m, stockQuantity = 1,
            imageUrl = "placeholder", categoryId,
        });

        // قاعدة يحرسها Money (خانات العملة) ⇒ 422 برمز ثابت، لا 500 ولا نص استثناء خام.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("InvalidMoney");
    }

    private Task<int> StockOf(int productId) => _api.WithDbAsync(db =>
        db.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());

    private Task<List<(StockMovementType Type, int QuantityChange)>> LedgerOf(int productId) => _api.WithDbAsync(async db =>
        (await db.StockMovements.Where(m => m.ProductId == productId)
            .Select(m => new { m.Type, m.QuantityChange }).ToListAsync())
        .Select(m => (m.Type, m.QuantityChange)).ToList());

    private sealed record PriceBody(decimal Price);
    private sealed record OrderTotalsBody(int OrderId, decimal Subtotal, decimal? DiscountAmount, decimal TotalAmount);
}
