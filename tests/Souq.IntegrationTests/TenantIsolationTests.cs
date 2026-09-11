using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// عزل المستأجرين عبر HTTP الحقيقي وSQL Server الحقيقي (MultiTenancy.md §5). متجر B — بمديره
// وعميله وزوّاره على مضيفه — يطلب موارد متجر A بمعرّفاتها الحقيقية:
//   • القراءة والتعديل والحذف ⇒ 404 (المورد "غير موجود" من منظوره — لا 403 يكشف وجوده).
//   • القوائم لا تُظهر صفوف A أبداً، والقوائم تحت مورد لـ A فارغة.
//   • لا مرجع لمورد A عند الكتابة (فئة، أب، منتج في طلب، كوبون، تقييم).
//   • توكن A على مضيف B ⇒ 401. حارس الكتابة والمرشّح بلا متجر يفشلان بصوت عالٍ.
// الجدول صريح (قرار مراجَع لكل نقطة)، واختبار الاكتمال يفشل إن ظهرت نقطة بمعرّف مورد لم تُضف إليه.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class TenantIsolationTests
{
    private enum Actor { Anonymous, Admin, Customer }
    private enum Resource { Product, Category, Coupon, Order }

    private sealed record ForeignCase(string Method, string Route, Resource Resource, Actor Actor, Func<HttpContent>? Body = null);

    // توقيعات PNG/MP4 صالحة كي يصل الرفع إلى حالة الاستخدام (لا يُرفض شكلاً قبل فحص المتجر).
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Mp4Bytes = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0, 0, 0, 0, 0];

    private static readonly ForeignCase[] ForeignResourceCases =
    [
        new("GET", "api/Products/{id:int}", Resource.Product, Actor.Anonymous),
        new("GET", "api/Products/{id:int}/related", Resource.Product, Actor.Anonymous),
        new("PUT", "api/Products/{id:int}", Resource.Product, Actor.Admin,
            () => JsonBody(new { nameAr = "منتج", description = "وصف", price = 5m, imageUrl = "x", categoryId = 1 })),
        new("DELETE", "api/Products/{id:int}", Resource.Product, Actor.Admin),
        new("POST", "api/Products/{id:int}/image", Resource.Product, Actor.Admin, () => FileBody(PngBytes, "a.png")),
        new("POST", "api/Products/{id:int}/video", Resource.Product, Actor.Admin, () => FileBody(Mp4Bytes, "a.mp4")),
        new("PUT", "api/Categories/{id:int}", Resource.Category, Actor.Admin,
            () => JsonBody(new { name = "فئة", slug = "foreign-edit" })),
        new("DELETE", "api/Categories/{id:int}", Resource.Category, Actor.Admin),
        new("PUT", "api/Coupons/{id:int}", Resource.Coupon, Actor.Admin,
            () => JsonBody(new { type = "Percentage", value = 50m, isActive = true })),
        new("DELETE", "api/Coupons/{id:int}", Resource.Coupon, Actor.Admin),
        new("GET", "api/Orders/{id:int}", Resource.Order, Actor.Admin),
        new("POST", "api/Orders/{id:int}/confirm-payment", Resource.Order, Actor.Admin),
        new("PUT", "api/Orders/{id:int}/status", Resource.Order, Actor.Admin, () => JsonBody(new { action = "Cancel" })),
        new("GET", "api/Orders/{id:int}/tracking", Resource.Order, Actor.Anonymous),
    ];

    // قوائم تحت منتج لـ A: 200 بلا أي صف (القائمة موجودة؛ المنتج "لا صفوف له" من منظور B).
    private static readonly (string Route, Actor Actor)[] ScopedListings =
    [
        ("api/admin/inventory/{productId:int}/movements", Actor.Admin),
        ("api/products/{productId:int}/reviews", Actor.Anonymous),
    ];

    // كتابة تحت منتج لـ A: تُرفض ولا تُنشئ شيئاً مرتبطاً بمتجر آخر.
    private static readonly (string Method, string Route, Actor Actor)[] WritesUnderForeignParent =
    [
        ("POST", "api/products/{productId:int}/reviews", Actor.Customer),
    ];

    private readonly SouqApiFactory _factory;

    public TenantIsolationTests(SouqApiFactory factory) => _factory = factory;

    [Fact]
    public void كل_نقطة_بمعرّف_مورد_مغطّاة_في_جدول_العزل()
    {
        _factory.CreateClient();
        var covered = ForeignResourceCases.Select(c => $"{c.Method} {c.Route}")
            .Concat(ScopedListings.Select(l => $"GET {l.Route}"))
            .Concat(WritesUnderForeignParent.Select(w => $"{w.Method} {w.Route}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.Parameters.Count > 0)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} {e.RoutePattern.RawText}"))
            .Where(endpoint => !covered.Contains(endpoint))
            .ToList();

        missing.Should().BeEmpty("كل نقطة تأخذ معرّف مورد متجر تُضاف لجدول العزل بقرار صريح");
    }

    [Fact]
    public async Task موارد_متجر_آخر_غير_موجودة_قراءةً_وتعديلاً_وحذفاً_والرد_404()
    {
        var s = await ArrangeAsync();

        foreach (var c in ForeignResourceCases)
        {
            var url = "/" + Regex.Replace(c.Route, @"\{[^}]+\}", s.AIds[c.Resource].ToString());
            var response = await ClientFor(s, c.Actor)
                .SendAsync(new HttpRequestMessage(new HttpMethod(c.Method), url) { Content = c.Body?.Invoke() });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{c.Method} {url} من متجر B يجب ألّا يرى مورد A");
        }

        // لم يتغيّر شيء في A: المنتج نشط، الفئة والكوبون موجودان، والطلب ما زال بانتظار الدفع.
        int productId = s.AIds[Resource.Product], categoryId = s.AIds[Resource.Category];
        int couponId = s.AIds[Resource.Coupon], orderId = s.AIds[Resource.Order];
        var state = await s.StoreA.WithDbAsync(async db => (
            ProductActive: await db.Products.Where(p => p.Id == productId).Select(p => p.IsActive).SingleAsync(),
            CategoryExists: await db.Categories.AnyAsync(c => c.Id == categoryId),
            CouponValue: await db.Coupons.Where(c => c.Id == couponId).Select(c => c.Value).SingleAsync(),
            OrderStatus: await db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()));
        state.Should().Be((true, true, 10m, OrderStatus.Pending));
    }

    [Fact]
    public async Task القوائم_تحت_مورد_متجر_آخر_فارغة()
    {
        var s = await ArrangeAsync();
        var productId = s.AIds[Resource.Product];

        // في A توجد حركة المخزون الابتدائي فعلاً — B لا يراها.
        (await CountAsync(s.AdminA, $"/api/admin/inventory/{productId}/movements")).Should().BeGreaterThan(0);

        foreach (var (route, actor) in ScopedListings)
        {
            var url = "/" + Regex.Replace(route, @"\{[^}]+\}", productId.ToString());
            (await CountAsync(ClientFor(s, actor), url)).Should().Be(0, $"GET {url} من متجر B");
        }
    }

    [Fact]
    public async Task قوائم_المتجر_لا_تُظهر_صفوف_متجر_آخر()
    {
        var s = await ArrangeAsync();
        var bProduct = await s.StoreB.CreateProductAsync(s.AdminB, price: 7m, stock: 2);

        (await IdsAsync(s.StoreB.Anonymous(), "/api/products?pageSize=100")).Should().Equal(bProduct);
        (await IdsAsync(s.AdminB, "/api/admin/inventory?pageSize=100")).Should().Equal(bProduct);
        (await IdsAsync(s.AdminB, "/api/admin/inventory/low-stock?pageSize=100")).Should().Equal(bProduct);
        (await IdsAsync(s.AdminB, "/api/coupons?pageSize=100")).Should().BeEmpty();
        (await IdsAsync(s.AdminB, "/api/orders?pageSize=100")).Should().BeEmpty();
        var bCategories = await s.StoreB.Anonymous().GetFromJsonAsync<List<TestApi.IdBody>>("/api/categories", TestApi.Json);
        bCategories!.Select(c => c.Id).Should().ContainSingle().And.NotContain(s.AIds[Resource.Category]);

        // A يرى صفوفه لا صفوف B.
        (await IdsAsync(s.StoreA.Anonymous(), "/api/products?pageSize=100"))
            .Should().Contain(s.AIds[Resource.Product]).And.NotContain(bProduct);
    }

    [Fact]
    public async Task لا_مرجع_لمورد_متجر_آخر_عند_الكتابة()
    {
        var s = await ArrangeAsync();
        int aCategory = s.AIds[Resource.Category], aProduct = s.AIds[Resource.Product];

        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/products", new
            {
                nameAr = "منتج", description = "وصف", price = 5m, stockQuantity = 1, imageUrl = "x", categoryId = aCategory,
            })))
            .Should().Be((HttpStatusCode.BadRequest, "CategoryNotFound"));

        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/categories",
                new { name = "فرعية", slug = $"child-{Guid.NewGuid():N}"[..20], parentId = aCategory })))
            .Should().Be((HttpStatusCode.BadRequest, "ParentNotFound"));

        var bProduct = await s.StoreB.CreateProductAsync(s.AdminB);
        (await ProblemAsync(await s.AdminB.PutAsJsonAsync($"/api/products/{bProduct}",
                new { nameAr = "منتج", description = "وصف", price = 5m, imageUrl = "x", categoryId = aCategory })))
            .Should().Be((HttpStatusCode.BadRequest, "CategoryNotFound"));

        (await ProblemAsync(await s.StoreB.PlaceOrderAsync(s.CustomerB, aProduct, 1)))
            .Should().Be((HttpStatusCode.BadRequest, "ProductNotFound"));

        (await ProblemAsync(await s.StoreB.Anonymous().GetAsync($"/api/coupons/apply?code={s.ACouponCode}&subtotal=100")))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "CouponNotFound"));

        (await ProblemAsync(await s.CustomerB.PostAsJsonAsync($"/api/products/{aProduct}/reviews",
                new { rating = 5, comment = "رائع" })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "NotEligible"));
    }

    [Fact]
    public async Task توكن_متجر_على_مضيف_متجر_آخر_يُرفض_بـ_401()
    {
        var s = await ArrangeAsync();
        var tokenA = await s.StoreA.AdminTokenAsync();
        var tokenB = await s.StoreB.AdminTokenAsync();

        (await s.StoreA.Authorized(tokenA).GetAsync("/api/coupons")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.StoreA.Authorized(tokenA, s.StoreB.Host).GetAsync("/api/coupons")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await s.StoreA.Authorized(tokenB, s.StoreA.Host).GetAsync("/api/coupons")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await s.StoreA.Authorized(tokenB, s.StoreA.Host).GetAsync("/api/orders/mine")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task التفرّد_داخل_المتجر_لا_على_المنصّة()
    {
        var s = await ArrangeAsync();

        var slug = $"shared-{Guid.NewGuid():N}"[..20];
        (await s.AdminA.PostAsJsonAsync("/api/categories", new { name = "مشتركة", slug })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await s.AdminB.PostAsJsonAsync("/api/categories", new { name = "مشتركة", slug })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/categories", new { name = "مكرّرة", slug })))
            .Should().Be((HttpStatusCode.Conflict, "SlugTaken"));

        var coupon = new { code = s.ACouponCode, type = "Percentage", value = 5m };
        (await s.AdminB.PostAsJsonAsync("/api/coupons", coupon)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/coupons", coupon)))
            .Should().Be((HttpStatusCode.Conflict, "DuplicateCode"));

        // الحسابات لكل متجر (D-06): البريد نفسه حسابان مستقلّان في متجرين، ومكرّر داخل المتجر نفسه يُرفض.
        var email = $"both-{Guid.NewGuid():N}@souq.test";
        await s.StoreA.NewCustomerAsync(email);
        await s.StoreB.NewCustomerAsync(email);
        var owners = await Task.WhenAll(
            s.StoreA.WithDbAsync(db => db.Customers.Where(c => c.Email == email).Select(c => c.TenantId).SingleAsync()),
            s.StoreB.WithDbAsync(db => db.Customers.Where(c => c.Email == email).Select(c => c.TenantId).SingleAsync()));
        owners.Should().OnlyHaveUniqueItems();
        (await ProblemAsync(await s.StoreB.Anonymous().PostAsJsonAsync("/api/auth/register",
                new { fullName = "مكرّر", email, password = "Customer-Pass-1" })))
            .Should().Be((HttpStatusCode.Conflict, "EmailTaken"));
    }

    [Fact]
    public async Task حارس_الكتابة_يرفض_تعديل_صف_متجر_آخر_ولا_يُحفظ_شيء()
    {
        var s = await ArrangeAsync();
        var productId = s.AIds[Resource.Product];

        await using (var scope = await _factory.TenantScopeAsync(s.B.Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // تجاوز متعمَّد للمرشّح يحاكي ثغرة قراءة — الحارس هو خط الدفاع الثاني.
            var foreign = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == productId);
            foreign.Deactivate();

            var act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<CrossTenantWriteException>();
        }

        (await s.StoreA.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.IsActive).SingleAsync()))
            .Should().BeTrue();
        _factory.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Critical && e.Message.Contains("cross-tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task إضافة_صف_بمتجر_صريح_غير_متجر_السياق_تُرفض()
    {
        var store = await _factory.CreateStoreAsync();
        var defaultTenant = await _factory.DefaultTenantAsync();

        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category("مزروعة", $"planted-{Guid.NewGuid():N}"[..20]);
        db.Categories.Add(category);
        db.Entry(category).Property(nameof(Category.TenantId)).CurrentValue = defaultTenant.Id;

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task بلا_سياق_متجر_لا_تُقرأ_ولا_تُكتب_بيانات_المتاجر()
    {
        _factory.CreateClient();

        await using (var none = _factory.Services.CreateAsyncScope())
        {
            var db = none.ServiceProvider.GetRequiredService<AppDbContext>();
            var read = () => db.Products.CountAsync();
            Chain((await read.Should().ThrowAsync<Exception>()).Which)
                .Should().Contain(e => e is TenantContextMissingException, "استعلام بلا متجر لا يعيد كل الصفوف أبداً");
        }

        await using (var platform = _factory.Services.CreateAsyncScope())
        {
            platform.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
            var db = platform.ServiceProvider.GetRequiredService<AppDbContext>();

            var read = () => db.Orders.AnyAsync();
            Chain((await read.Should().ThrowAsync<Exception>()).Which).Should().Contain(e => e is TenantContextMissingException);

            db.Categories.Add(new Category("منصّة", $"platform-{Guid.NewGuid():N}"[..20]));
            var write = () => db.SaveChangesAsync();
            await write.Should().ThrowAsync<TenantContextMissingException>();
        }
    }

    [Fact]
    public async Task ملفات_متجر_لا_تُخدَم_على_مضيف_متجر_آخر()
    {
        var s = await ArrangeAsync();

        var upload = await s.AdminA.PostAsync($"/api/products/{s.AIds[Resource.Product]}/image", FileBody(PngBytes, "a.png"));
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var url = (await upload.Content.ReadFromJsonAsync<ImageBody>(TestApi.Json))!.ImageUrl;

        url.Should().StartWith($"/uploads/tenants/{(await s.StoreA.TenantAsync()).Id}/images/");
        (await s.StoreA.Anonymous().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.StoreB.Anonymous().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── الإعداد: موارد حقيقية في A عبر الـ API، ومتجر B فعلي بمديره وعميله على مضيفه ──
    private sealed record Arranged(
        TestApi StoreA, TestApi StoreB, TestStore B, HttpClient AdminA, HttpClient AdminB, HttpClient CustomerB,
        IReadOnlyDictionary<Resource, int> AIds, string ACouponCode);

    private async Task<Arranged> ArrangeAsync()
    {
        var storeA = new TestApi(_factory);
        var adminA = await storeA.AdminAsync();
        var categoryId = await storeA.CreateCategoryAsync(adminA);
        var productId = await storeA.CreateProductAsync(adminA, price: 10m, stock: 5, categoryId: categoryId);
        var couponCode = $"ISO{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var couponResponse = await adminA.PostAsJsonAsync("/api/coupons", new { code = couponCode, type = "Percentage", value = 10m });
        couponResponse.StatusCode.Should().Be(HttpStatusCode.Created, await couponResponse.Content.ReadAsStringAsync());
        var couponId = (await couponResponse.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        var (customerA, _) = await storeA.NewCustomerAsync();
        var placed = await storeA.PlaceOrderAsync(customerA, productId, 1);
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;

        var b = await _factory.CreateStoreAsync();
        var storeB = storeA.ForStore(b);
        return new Arranged(storeA, storeB, b, adminA, await storeB.AdminAsync(), (await storeB.NewCustomerAsync()).Client,
            new Dictionary<Resource, int>
            {
                [Resource.Product] = productId, [Resource.Category] = categoryId,
                [Resource.Coupon] = couponId, [Resource.Order] = orderId,
            },
            couponCode);
    }

    private static HttpClient ClientFor(Arranged s, Actor actor) => actor switch
    {
        Actor.Admin => s.AdminB,
        Actor.Customer => s.CustomerB,
        _ => s.StoreB.Anonymous(),
    };

    private static async Task<List<int>> IdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>(url, TestApi.Json))!.Items.Select(i => i.Id).ToList();

    private static async Task<int> CountAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return (await response.Content.ReadFromJsonAsync<CountBody>(TestApi.Json))!.TotalCount;
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private static HttpContent JsonBody(object body) => JsonContent.Create(body, options: TestApi.Json);

    private static HttpContent FileBody(byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return form;
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current;
    }

    private sealed record CountBody(int TotalCount);
    private sealed record ImageBody(string ImageUrl);
}
