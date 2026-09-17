using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Features.Products.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// المتغيّرات V1 (ProductVariants.md §11، ADR-0039) عبر HTTP وSQL Server الحقيقيين: هوية المتغيّر من السلة إلى التسعير
// فالطلب فالدفع فالمخزون، لقطة SKU على سطر الطلب، رفض المتغيّر الغريب والمعطّل وغياب التحديد، مسارات المخزون بالمتغيّر،
// وقيود القاعدة. منتج بمتغيّرين لا يُنشأ من التطبيق قبل خيارات المتغيّرات (V2): يُزرع بـ Product.AddVariant الداخلي ويُفتح
// مخزونه بمنفذ Inventory نفسه الذي يستعمله إنشاء المنتج. العزل بين المتاجر في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ProductVariantTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public ProductVariantTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task متغيّرا_المنتج_نفسه_من_السلة_إلى_الدفع_سطران_بلقطتيهما_ومخزون_كلٍّ_وحده()
    {
        var admin = await _api.AdminAsync();
        var (productId, small, large, smallSku, largeSku) = await TwoVariantProductAsync(admin, smallStock: 5, largeStock: 3);
        var customer = (await _api.NewCustomerAsync()).Client;

        await AddAsync(customer, new { productId, variantId = small, quantity = 1 });
        await AddAsync(customer, new { productId, variantId = large, quantity = 2 });
        var basket = await AddAsync(customer, new { productId, variantId = small, quantity = 1 });

        basket.Lines.Select(l => (l.ProductId, l.VariantId, l.Quantity, l.UnitPrice, l.Sellable))
            .Should().Equal((productId, small, 2, 20m, true), (productId, large, 2, 25m, true));
        (basket.Subtotal, basket.ReadyForCheckout).Should().Be((90m, true));

        // مسار المنتج ملتبس حين له سطران؛ مسار المتغيّر يصيب سطره وحده.
        (await ProblemAsync(await customer.PutAsJsonAsync($"/api/basket/items/{productId}", new { quantity = 1 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "VariantRequired"));
        (await customer.PutAsJsonAsync($"/api/basket/items/variants/{large}", new { quantity = 2 })).EnsureSuccessStatusCode();

        var placed = await customer.PostAsJsonAsync("/api/orders", new { shippingAddress = "عمّان — عنوان اختبار" });
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var created = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!;
        created.TotalAmount.Should().Be(90m, "إجمالي الطلب هو إجمالي السلة بأسعار متغيّراتها");

        var order = await OrderAsync(customer, created.OrderId);
        order.Items.Select(i => (i.ProductId, i.VariantId, i.UnitPrice, i.Quantity, i.Sku, i.VariantLabel))
            .Should().BeEquivalentTo(new[] { (productId, small, 20m, 2, smallSku, (string?)null), (productId, large, 25m, 2, largeSku, (string?)null) });

        // حجز لكل متغيّر من مخزونه وحده، والدفع يلتزمهما ويستهلك سطرَي السلة.
        (await StockAsync(small)).Should().Be((5, 2));
        (await StockAsync(large)).Should().Be((3, 2));
        (await customer.PostAsync($"/api/orders/{created.OrderId}/confirm-payment", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StockAsync(small)).Should().Be((3, 0));
        (await StockAsync(large)).Should().Be((1, 0));
        (await ReadBasketAsync(await customer.GetAsync("/api/basket"))).Lines.Should().BeEmpty();

        // اللقطة لا تتبع الكتالوج: تغيير SKU المتغيّر بعد الشراء لا يغيّر الطلب.
        await _api.WithDbAsync(async db =>
        {
            var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId);
            product.SetPricing(new Money(19m, "JOD"), null, $"NEW{Guid.NewGuid():N}"[..12]);
            return await db.SaveChangesAsync();
        });
        (await OrderAsync(customer, created.OrderId)).Items.Single(i => i.VariantId == small)
            .Should().BeEquivalentTo(new { Sku = smallSku, UnitPrice = 20m });
    }

    [Fact]
    public async Task منتج_بمتغيّر_واحد_يعمل_بلا_معرفة_المتغيّرات_وسطر_طلبه_يحمل_متغيّره_الافتراضي()
    {
        var admin = await _api.AdminAsync();
        var sku = $"ONE{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(await _api.CreateCategoryAsync(admin), 7m, 4, sku: sku));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var productId = (await created.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        var defaultVariant = await DefaultVariantAsync(productId);
        var customer = (await _api.NewCustomerAsync()).Client;

        // عقود ما قبل المتغيّرات كما هي: الإضافة والتعديل والحذف بالمنتج، والطلب بأسطر بلا متغيّر.
        (await AddAsync(customer, new { productId, quantity = 1 })).Lines.Single().VariantId.Should().Be(defaultVariant);
        (await customer.PutAsJsonAsync($"/api/basket/items/{productId}", new { quantity = 2 })).EnsureSuccessStatusCode();
        (await customer.DeleteAsync($"/api/basket/items/{productId}")).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/admin/inventory/{productId}/adjustments", new { delta = 1, reason = "جرد" })).EnsureSuccessStatusCode();

        var placed = await _api.PlaceOrderAsync(customer, productId, 2);
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;

        (await OrderAsync(customer, orderId)).Items.Should().ContainSingle().Which
            .Should().BeEquivalentTo(new { ProductId = productId, VariantId = defaultVariant, Sku = sku, VariantLabel = (string?)null, Quantity = 2 });
        (await StockAsync(defaultVariant)).Should().Be((5, 2));
    }

    [Fact]
    public async Task بلا_تحديد_لمنتج_متعدّد_يُطلب_المتغيّر_والغريب_يُرفض_والمعطّل_لا_يُشترى()
    {
        var admin = await _api.AdminAsync();
        var (productId, small, large, _, _) = await TwoVariantProductAsync(admin, smallStock: 5, largeStock: 5);
        var otherProduct = await _api.CreateProductAsync(admin, price: 3m, stock: 5);
        var customer = (await _api.NewCustomerAsync()).Client;

        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "VariantRequired"));
        (await ProblemAsync(await _api.PlaceOrderAsync(customer, productId, 1)))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "VariantRequired"));
        // متغيّر من منتج آخر في المتجر نفسه: لا يُضاف مع المنتج الآخر، ولا يُسعَّر به طلب.
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items", new { productId = otherProduct, variantId = large, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/orders", new
            {
                shippingAddress = "عمّان — عنوان اختبار",
                items = new[] { new { productId = otherProduct, quantity = 1, variantId = large } },
            })))
            .Should().Be((HttpStatusCode.BadRequest, "ProductNotFound"));

        // المتغيّر يُعطَّل وهو في السلة: سطره غير قابل للبيع، والدفع يرفضه، ولا يُضاف من جديد.
        await AddAsync(customer, new { productId, variantId = large, quantity = 1 });
        await _api.WithDbAsync(async db =>
        {
            var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId);
            product.DeactivateVariant(large);
            return await db.SaveChangesAsync();
        });

        var basket = await ReadBasketAsync(await customer.GetAsync("/api/basket"));
        (basket.Lines.Single().Sellable, basket.ReadyForCheckout, basket.Total).Should().Be((false, false, 0m));
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/orders", new { shippingAddress = "عمّان — عنوان اختبار" })))
            .Should().Be((HttpStatusCode.BadRequest, "ProductNotFound"));
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items", new { productId, variantId = large, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));

        // بقي للمنتج متغيّر نشط واحد: يعود يُشترى بلا تحديد كمنتج بسيط.
        (await customer.DeleteAsync($"/api/basket/items/variants/{large}")).EnsureSuccessStatusCode();
        (await AddAsync(customer, new { productId, quantity = 1 })).Lines.Single().VariantId.Should().Be(small);
        (await _api.WithDbAsync(db => db.StockReservations.CountAsync(r => db.InventoryItems.Any(i => i.Id == r.InventoryItemId && i.VariantId == large))))
            .Should().Be(0);
    }

    [Fact]
    public async Task مخزون_الإدارة_بالمتغيّر_يصيب_صفّه_ومسار_المنتج_يرفض_منتجاً_متعدّداً()
    {
        var admin = await _api.AdminAsync();
        var (productId, small, large, smallSku, largeSku) = await TwoVariantProductAsync(admin, smallStock: 5, largeStock: 3);

        (await ProblemAsync(await admin.PostAsJsonAsync($"/api/admin/inventory/{productId}/adjustments", new { delta = 4, reason = "جرد" })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "VariantRequired"));
        (await ProblemAsync(await admin.PutAsJsonAsync($"/api/admin/inventory/{productId}/threshold", new { lowStockThreshold = 1 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "VariantRequired"));

        var adjusted = await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{large}/adjustments", new { delta = 4, reason = "توريد" });
        adjusted.StatusCode.Should().Be(HttpStatusCode.OK, await adjusted.Content.ReadAsStringAsync());
        (await adjusted.Content.ReadFromJsonAsync<StockLevelBody>(TestApi.Json))
            .Should().Be(new StockLevelBody(productId, large, 7, 0, 7, 5, false));
        (await admin.PutAsJsonAsync($"/api/admin/inventory/variants/{large}/threshold", new { lowStockThreshold = 1 })).EnsureSuccessStatusCode();
        (await ProblemAsync(await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{large}/adjustments", new { delta = -8, reason = "تلف" })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidInventoryOperation"));
        (await StockAsync(small)).Should().Be((5, 0));
        (await StockAsync(large)).Should().Be((7, 0));

        var variantMovements = await admin.GetFromJsonAsync<TestApi.PageBody<MovementBody>>(
            $"/api/admin/inventory/variants/{large}/movements", TestApi.Json);
        variantMovements!.Items.Select(m => (m.VariantId, m.QuantityChange)).Should().Equal((large, 4), (large, 3));
        var productMovements = await admin.GetFromJsonAsync<TestApi.PageBody<MovementBody>>(
            $"/api/admin/inventory/{productId}/movements", TestApi.Json);
        productMovements!.Items.Select(m => m.VariantId).Should().BeEquivalentTo(new[] { large, large, small });

        // الجرد صفّ لكل متغيّر بـ SKU متغيّره. القائمة مرتّبة بالأقلّ متاحاً في قاعدة مشتركة، فتُقرأ صفحةً صفحة حتى صفّي المنتج.
        var rows = new List<InventoryRowBody>();
        for (var page = 1; ; page++)
        {
            var inventory = (await admin.GetFromJsonAsync<TestApi.PageBody<InventoryRowBody>>(
                $"/api/admin/inventory?page={page}&pageSize=100", TestApi.Json))!;
            rows.AddRange(inventory.Items.Where(r => r.Id == productId));
            if (!inventory.HasNext) break;
        }
        rows.Select(r => (r.VariantId, r.Sku, r.OnHand)).Should().BeEquivalentTo(new[] { (small, smallSku, 5), (large, largeSku, 7) });

        (await _api.WithDbAsync(db => db.AuditEntries.CountAsync(a =>
            a.Action == "inventory.adjusted" && a.TargetType == "ProductVariant" && a.TargetId == large.ToString())))
            .Should().Be(1, "التصحيح الناجح وحده يُدقَّق بمرجع المتغيّر");
    }

    [Fact]
    public async Task آخر_قطعة_من_متغيّر_تُباع_مرّة_واحدة_تحت_التزامن_والمتغيّر_الآخر_لا_يتأثّر()
    {
        var admin = await _api.AdminAsync();
        var (productId, small, large, _, _) = await TwoVariantProductAsync(admin, smallStock: 10, largeStock: 1);
        var customers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _api.NewCustomerAsync()));

        var responses = await Task.WhenAll(customers.Select(c => c.Client.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = new[] { new { productId, quantity = 1, variantId = large }, new { productId, quantity = 1, variantId = small } },
        })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            (await ProblemAsync(rejected)).Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));
        (await StockAsync(large)).Should().Be((1, 1));
        (await StockAsync(small)).Should().Be((10, 1), "الطلب الخاسر أُلغيت معاملته كلها — لا حجز يتيم على المتغيّر الآخر");
    }

    [Fact]
    public async Task القاعدة_تحرس_سطراً_واحداً_لكل_متغيّر_ومتغيّراً_إلزامياً_وافتراضياً_نشطاً()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 5m, stock: 5);
        var customer = (await _api.NewCustomerAsync()).Client;
        var placed = await _api.PlaceOrderAsync(customer, productId, 1);
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        var defaultVariant = await DefaultVariantAsync(productId);

        // خطّ دفاع أخير تحت قواعد الكيان — SQL مباشر يتجاوز Order.AddItem وProduct.DeactivateVariant عمداً.
        var duplicateLine = () => _api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [OrderItems] ([TenantId], [OrderId], [ProductId], [VariantId], [ProductName], [UnitPrice], [Currency], [Quantity], [CreatedAt])
            SELECT [TenantId], [OrderId], [ProductId], [VariantId], [ProductName], [UnitPrice], [Currency], 1, [CreatedAt]
            FROM [OrderItems] WHERE [OrderId] = {orderId}
            """));
        var lineWithoutVariant = () => _api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [OrderItems] ([TenantId], [OrderId], [ProductId], [ProductName], [UnitPrice], [Currency], [Quantity], [CreatedAt])
            SELECT [TenantId], [OrderId], [ProductId], [ProductName], [UnitPrice], [Currency], 1, [CreatedAt]
            FROM [OrderItems] WHERE [OrderId] = {orderId}
            """));
        var inactiveDefault = () => _api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ProductVariants] SET [IsActive] = 0 WHERE [Id] = {defaultVariant}"));

        await duplicateLine.Should().ThrowAsync<SqlException>();
        await lineWithoutVariant.Should().ThrowAsync<SqlException>();
        await inactiveDefault.Should().ThrowAsync<SqlException>();
        (await _api.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).SelectMany(o => o.Items).CountAsync())).Should().Be(1);
    }

    // ── أدوات ──

    // منتج بسعر 20 (المتغيّر الافتراضي) ومتغيّر ثانٍ بسعر 25، لكلٍّ SKU ومخزون.
    private async Task<(int ProductId, int Small, int Large, string SmallSku, string LargeSku)> TwoVariantProductAsync(
        HttpClient admin, int smallStock, int largeStock)
    {
        var smallSku = $"S{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var largeSku = $"L{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync("/api/products",
            TestApi.ProductBody(await _api.CreateCategoryAsync(admin), 20m, smallStock, sku: smallSku));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var productId = (await created.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;

        await using var scope = await _factory.TenantScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId);
        var large = product.AddVariant(new Money(25m, product.Price.Currency), sku: largeSku);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IVariantStockInitializer>()
            .InitializeAsync(productId, large.Id, largeStock, InventoryItem.DefaultLowStockThreshold, CancellationToken.None);

        return (productId, product.DefaultVariant.Id, large.Id, smallSku, largeSku);
    }

    private Task<int> DefaultVariantAsync(int productId) => _api.WithDbAsync(db =>
        db.Products.Where(p => p.Id == productId).SelectMany(p => p.Variants).Where(v => v.IsDefault).Select(v => v.Id).SingleAsync());

    private Task<(int OnHand, int Reserved)> StockAsync(int variantId) => _api.WithDbAsync(async db =>
    {
        var item = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.VariantId == variantId);
        return (item.OnHand, item.Reserved);
    });

    private static async Task<TestApi.BasketBody> AddAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/basket/items", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadBasketAsync(response);
    }

    private static async Task<TestApi.BasketBody> ReadBasketAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TestApi.BasketBody>(TestApi.Json))!;

    private static async Task<OrderBody> OrderAsync(HttpClient client, int orderId) =>
        (await client.GetFromJsonAsync<OrderBody>($"/api/orders/{orderId}", TestApi.Json))!;

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record OrderBody(List<OrderItemBody> Items);
    private sealed record OrderItemBody(
        int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal, int VariantId, string? VariantLabel, string? Sku);
    private sealed record StockLevelBody(
        int ProductId, int VariantId, int OnHand, int Reserved, int Available, int LowStockThreshold, bool IsLowStock);
    private sealed record MovementBody(int Id, string Type, int QuantityChange, int NewQuantity, int VariantId);
    private sealed record InventoryRowBody(int Id, int VariantId, string? Sku, int OnHand, int LowStockThreshold);
}
