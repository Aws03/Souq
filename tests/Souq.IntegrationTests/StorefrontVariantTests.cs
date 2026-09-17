using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;
using static Souq.IntegrationTests.Infrastructure.VariantAdminApi;

namespace Souq.IntegrationTests;

// ============================================================================
// اختيار المتغيّر في واجهة المتجر (ProductVariants.md V3، ADR-0041) عبر HTTP وSQL Server الحقيقيين:
//   • عقد صفحة المنتج: الخيارات وقيمها والمتغيّرات النشطة بمتاحها — والمعطّل وقيمته الخاصّة غير موجودين إطلاقاً.
//   • السعر "ابتداءً من" أرخص متغيّر يمكن شراؤه الآن، وتغيّره حين ينفد الأرخص، والتصفية والترتيب على المعنى نفسه.
//   • المسار الكامل: اختيار ⇒ سلة (سطران بوصفيهما) ⇒ تسعير ⇒ طلب ⇒ دفع ⇒ عرض الطلب وبريده.
//   • كل معرّف متغيّر مرفوض: معطّل، من منتج آخر، قديم، وبلا تحديد لمنتج متعدّد؛ والمخزون يتغيّر بين التحميل والإضافة.
//   • منتج بسيط (بلا خيارات) عقده كما كان قبل V3 حرفياً.
// عزل المتاجر لهذه المسارات في TenantIsolationTests؛ إدارة الخيارات في ProductOptionAdminTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class StorefrontVariantTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public StorefrontVariantTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task صفحة_المنتج_تحمل_خياراته_ومتغيّراته_النشطة_بمتاحها_ولا_تذكر_المعطّل()
    {
        var admin = await _api.AdminAsync();
        var shirt = await ShirtAsync(admin);

        // متغيّر رابع بقيمة خاصّة به (XL) ثم يُعطَّل: لا هو ولا قيمته يظهران للمتسوّق (قرار V3-a).
        (await SetOptionsAsync(admin, shirt.ProductId, WithExtraSize(shirt.Product, "XL"))).EnsureSuccessStatusCode();
        var withXl = await ProductAsync(admin, shirt.ProductId);
        var xl = (await CreateVariantsOkAsync(admin, shirt.ProductId, new
        {
            optionValueIds = new[] { ValueId(withXl, "المقاس", "XL"), ValueId(withXl, "اللون", "أحمر") },
            price = 30m, initialStock = 3,
        })).Single();
        (await admin.PutAsJsonAsync($"/api/admin/products/{shirt.ProductId}/variants/{xl}/status", new { isActive = false }))
            .EnsureSuccessStatusCode();

        var product = await StorefrontAsync(_api.Anonymous(), shirt.ProductId);

        product.Options.Select(o => o.Names["ar"]).Should().Equal("المقاس", "اللون");
        // تُعرض القيم التي يستخدمها متغيّر معروض وحدها: XL لا يستخدمها إلا المعطّل، وM لم يُنشئ لها التاجر متغيّراً —
        // وكلتاهما ليست خياراً حقيقياً للمتسوّق. أما أزرق فيستخدمها متغيّر نشط نفد ⇒ تُعرض (معطّلة في الواجهة، P-08c).
        product.Options[0].Values.Select(v => v.Names["ar"]).Should().Equal(["S", "L"]);
        product.Options[1].Values.Select(v => v.Names["ar"]).Should().Equal(["أحمر", "أزرق"]);
        product.Variants.Should().HaveCount(3).And.NotContain(v => v.Id == xl);
        product.Variants.Select(v => (v.Id, v.Price, v.Available)).Should().BeEquivalentTo(new[]
        {
            (shirt.SmallRed, 20m, 5), (shirt.SmallBlue, 20m, 0), (shirt.LargeRed, 25m, 4),
        });
        product.Variants.Should().OnlyContain(v => v.OptionValueIds.Count == 2);
        // "ابتداءً من 20": أرخص ما يمكن شراؤه، وأغلى ما يمكن شراؤه 25 ⇒ نطاق حقيقي.
        (product.Price, product.PriceIsFrom).Should().Be((20m, true));
        product.StockQuantity.Should().Be(9, "مجموع متاح المتغيّرات النشطة");
    }

    [Fact]
    public async Task سعر_من_يتبع_ما_يمكن_شراؤه_والتصفية_والترتيب_على_المعنى_نفسه()
    {
        var admin = await _api.AdminAsync();
        var shirt = await ShirtAsync(admin);
        var anonymous = _api.Anonymous();
        var categoryId = await CategoryOfAsync(shirt.ProductId);

        (await StorefrontAsync(anonymous, shirt.ProductId)).Price.Should().Be(20m);

        // نفد أرخص متغيّر (S / أحمر): السعر المعروض يصعد إلى أرخص ما **يمكن** شراؤه فعلاً، لا إلى سعر لا يُشترى به.
        (await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{shirt.SmallRed}/adjustments", new { delta = -5, reason = "بيع" }))
            .EnsureSuccessStatusCode();
        var after = await StorefrontAsync(anonymous, shirt.ProductId);
        (after.Price, after.PriceIsFrom).Should().Be((25m, false), "بقي متغيّر واحد يمكن شراؤه بسعر واحد");

        // التصفية والترتيب يستعملان السعر نفسه: نطاق يشمل 25 يُطابق، ونطاق حول 20 وحده لا.
        (await ListIdsAsync(anonymous, $"/api/products?categoryIds={categoryId}&minPrice=24&maxPrice=26&pageSize=50"))
            .Should().Contain(shirt.ProductId);
        (await ListIdsAsync(anonymous, $"/api/products?categoryIds={categoryId}&minPrice=19&maxPrice=21&pageSize=50"))
            .Should().NotContain(shirt.ProductId, "لا متغيّر يمكن شراؤه بسعر في هذا النطاق");

        var cheaper = await _api.CreateProductAsync(admin, price: 7m, stock: 2, categoryId: categoryId);
        (await ListIdsAsync(anonymous, $"/api/products?categoryIds={categoryId}&sortBy=PriceAsc&pageSize=50"))
            .Should().ContainInOrder(cheaper, shirt.ProductId);

        // نفد كل ما يُعرض: المنتج يبقى في القائمة وفي صفحته غير متاح بسعر متغيّر حقيقي (قرار V3-b).
        (await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{shirt.LargeRed}/adjustments", new { delta = -4, reason = "بيع" }))
            .EnsureSuccessStatusCode();
        var soldOut = await StorefrontAsync(anonymous, shirt.ProductId);
        (soldOut.Price, soldOut.PriceIsFrom, soldOut.StockQuantity).Should().Be((20m, false, 0));
        soldOut.Variants.Should().OnlyContain(v => v.Available == 0);
        (await ListIdsAsync(anonymous, $"/api/products?categoryIds={categoryId}&pageSize=50")).Should().Contain(shirt.ProductId);
    }

    [Fact]
    public async Task العروض_تتبع_متغيّراً_يمكن_شراؤه_بسعر_مقارنة()
    {
        var admin = await _api.AdminAsync();
        var shirt = await ShirtAsync(admin);
        var anonymous = _api.Anonymous();
        var categoryId = await CategoryOfAsync(shirt.ProductId);

        (await ListIdsAsync(anonymous, $"/api/products?categoryIds={categoryId}&onSale=true&pageSize=50"))
            .Should().NotContain(shirt.ProductId);

        // خصم على المتغيّر الكبير (25 بدل 30): المنتج يدخل العروض، وسعره المعروض يبقى أرخص ما يمكن شراؤه (20).
        (await admin.PutAsJsonAsync($"/api/admin/products/{shirt.ProductId}/variants/{shirt.LargeRed}",
            new { price = 25m, compareAtPrice = 30m })).EnsureSuccessStatusCode();

        (await ListIdsAsync(anonymous, $"/api/products?categoryIds={categoryId}&onSale=true&pageSize=50"))
            .Should().Contain(shirt.ProductId);
        (await StorefrontAsync(anonymous, shirt.ProductId)).Price.Should().Be(20m);

        // المتغيّر المخفَّض وحده نفد ⇒ لا خصم يمكن شراؤه ⇒ يخرج من العروض.
        (await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{shirt.LargeRed}/adjustments", new { delta = -4, reason = "بيع" }))
            .EnsureSuccessStatusCode();
        (await ListIdsAsync(anonymous, $"/api/products?categoryIds={categoryId}&onSale=true&pageSize=50"))
            .Should().NotContain(shirt.ProductId);
    }

    [Fact]
    public async Task المتسوّق_يختار_مقاسين_فيصيران_سطرين_بوصفيهما_إلى_الطلب_وبريده()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await store.AdminAsync();
        var shirt = await ShirtAsync(admin, store);
        var (customer, email) = await store.NewCustomerAsync();

        var first = await AddAsync(customer, new { productId = shirt.ProductId, variantId = shirt.SmallRed, quantity = 2 });
        first.Lines.Should().ContainSingle().Which.VariantLabel.Should().Be("S / أحمر", "الوصف الحيّ من خيارات المنتج");
        var basket = await AddAsync(customer, new { productId = shirt.ProductId, variantId = shirt.LargeRed, quantity = 1 });

        basket.Lines.Select(l => (l.VariantId, l.Quantity, l.UnitPrice, l.VariantLabel))
            .Should().BeEquivalentTo(new[] { (shirt.SmallRed, 2, 20m, "S / أحمر"), (shirt.LargeRed, 1, 25m, "L / أحمر") });
        basket.Subtotal.Should().Be(65m);

        var placed = await customer.PostAsJsonAsync("/api/orders", new { shippingAddress = "عمّان — عنوان اختبار" });
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null)).EnsureSuccessStatusCode();
        await _factory.DispatchNotificationsAsync();

        var order = (await customer.GetFromJsonAsync<OrderBody>($"/api/orders/{orderId}", TestApi.Json))!;
        order.Items.Select(i => (i.VariantId, i.Quantity, i.VariantLabel))
            .Should().BeEquivalentTo(new[] { (shirt.SmallRed, 2, "S / أحمر"), (shirt.LargeRed, 1, "L / أحمر") });

        var confirmation = _factory.Emails.LastTo(email, Souq.Application.Common.Notifications.EmailTemplate.OrderConfirmed)!;
        confirmation.TextBody.Should().Contain("S / أحمر").And.Contain("L / أحمر");
        confirmation.HtmlBody.Should().Contain("S / أحمر");

        // اللقطة لا تتبع الكتالوج: إعادة تسمية القيمة بعد الشراء لا تغيّر ما يقرؤه المشتري على طلبه.
        var product = await ProductAsync(admin, shirt.ProductId);
        var renamed = Current(product);
        renamed[0]["values"] = new List<object>
        {
            new { id = (int?)ValueId(product, "المقاس", "S"), names = Names("صغير") },
            new { id = (int?)ValueId(product, "المقاس", "M"), names = Names("M") },
            new { id = (int?)ValueId(product, "المقاس", "L"), names = Names("L") },
        };
        (await SetOptionsAsync(admin, shirt.ProductId, renamed)).EnsureSuccessStatusCode();
        (await customer.GetFromJsonAsync<OrderBody>($"/api/orders/{orderId}", TestApi.Json))!
            .Items.Select(i => i.VariantLabel).Should().BeEquivalentTo(new[] { "S / أحمر", "L / أحمر" });
    }

    [Fact]
    public async Task كل_متغيّر_مرفوض_يُرفض_والمخزون_يتغيّر_بين_التحميل_والإضافة()
    {
        var admin = await _api.AdminAsync();
        var shirt = await ShirtAsync(admin);
        var other = await _api.CreateProductAsync(admin, price: 4m, stock: 5);
        var customer = (await _api.NewCustomerAsync()).Client;

        // بلا تحديد لمنتج متعدّد المتغيّرات: يُطلب التحديد لا يُفترض مقاس.
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items", new { productId = shirt.ProductId, quantity = 1 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "VariantRequired"));

        // متغيّر منتج آخر، ومعرّف لا وجود له: غير موجود — لا تسعير بمتغيّر لا يخصّ المنتج.
        var otherVariant = await DefaultVariantAsync(other);
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items",
                new { productId = shirt.ProductId, variantId = otherVariant, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items",
                new { productId = shirt.ProductId, variantId = 999999, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));

        // متغيّر نفد: لا يُضاف بكمية أكبر من متاحه (المخزون حكم الخادم لا حالة الصفحة).
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items",
                new { productId = shirt.ProductId, variantId = shirt.SmallBlue, quantity = 1 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));

        // نفد المتغيّر بين تحميل الصفحة والإضافة (صفحة قديمة بيدها متاح 5): الخادم يرفض بما يملكه الآن.
        (await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{shirt.SmallRed}/adjustments", new { delta = -5, reason = "بيع" }))
            .EnsureSuccessStatusCode();
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items",
                new { productId = shirt.ProductId, variantId = shirt.SmallRed, quantity = 1 })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InsufficientStock"));

        // متغيّر عُطّل بعد أن رآه المتسوّق: لم يعد موجوداً عنده.
        (await admin.PutAsJsonAsync($"/api/admin/products/{shirt.ProductId}/variants/{shirt.LargeRed}/status", new { isActive = false }))
            .EnsureSuccessStatusCode();
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/basket/items",
                new { productId = shirt.ProductId, variantId = shirt.LargeRed, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await StorefrontAsync(_api.Anonymous(), shirt.ProductId)).Variants.Should().NotContain(v => v.Id == shirt.LargeRed);

        // لا سطر دخل سلّة العميل من كل ما رُفض أعلاه.
        (await _api.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            db.Baskets.SelectMany(b => b.Lines).Where(l => l.ProductId == shirt.ProductId)))).Should().Be(0);
    }

    [Fact]
    public async Task منتج_بلا_خيارات_عقده_كما_كان_ويُضاف_بلا_معرّف_متغيّر()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 11m, stock: 4);
        var customer = (await _api.NewCustomerAsync()).Client;

        var product = await StorefrontAsync(_api.Anonymous(), productId);
        (product.Options, product.Variants, product.PriceIsFrom).Should().Be((null, null, false));
        (product.Price, product.StockQuantity).Should().Be((11m, 4));

        var basket = await AddAsync(customer, new { productId, quantity = 2 });
        basket.Lines.Should().ContainSingle().Which.VariantLabel.Should().BeNull();
        basket.Lines.Single().VariantId.Should().Be(await DefaultVariantAsync(productId));
    }

    [Fact]
    public async Task المفضّلة_تُسعِّر_بما_يمكن_شراؤه()
    {
        var admin = await _api.AdminAsync();
        var shirt = await ShirtAsync(admin);
        var customer = (await _api.NewCustomerAsync()).Client;

        (await customer.PutAsync($"/api/wishlist/{shirt.ProductId}", null)).EnsureSuccessStatusCode();
        (await WishlistAsync(customer)).Single().Price.Should().Be(20m);

        (await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{shirt.SmallRed}/adjustments", new { delta = -5, reason = "بيع" }))
            .EnsureSuccessStatusCode();
        (await WishlistAsync(customer)).Single().Price.Should().Be(25m, "أرخص ما يمكن شراؤه الآن");
    }

    // ── أدوات ──

    // قميص بخيارَي المقاس (S, M, L) واللون (أحمر، أزرق) وثلاثة متغيّرات: S/أحمر بـ20 (متاح 5)، S/أزرق بـ20 (نفد)،
    // L/أحمر بـ25 (متاح 4). M بلا متغيّر — تركيبة لم يُنشئها التاجر.
    private async Task<Shirt> ShirtAsync(HttpClient admin, TestApi? store = null)
    {
        var api = store ?? _api;
        var productId = await api.CreateProductAsync(admin, price: 20m, stock: 5, name: $"قميص {Guid.NewGuid():N}"[..14]);
        (await SetOptionsAsync(admin, productId,
        [
            Option("المقاس", ["S", "M", "L"], existing: 0, en: "Size"),
            Option("اللون", ["أحمر", "أزرق"], existing: 0, en: "Colour"),
        ])).EnsureSuccessStatusCode();

        var product = await ProductAsync(admin, productId);
        var created = await CreateVariantsOkAsync(admin, productId,
            new { optionValueIds = new[] { ValueId(product, "المقاس", "S"), ValueId(product, "اللون", "أزرق") }, price = 20m, initialStock = 0 },
            new { optionValueIds = new[] { ValueId(product, "المقاس", "L"), ValueId(product, "اللون", "أحمر") }, price = 25m, initialStock = 4 });

        return new Shirt(productId, product.Variants.Single(v => v.IsDefault).Id, created[0], created[1],
            await ProductAsync(admin, productId));
    }

    private static IEnumerable<object> WithExtraSize(AdminProductBody product, string size)
    {
        var definition = Current(product);
        var values = (List<object>)definition[0]["values"]!;
        values.Add(new { id = (int?)null, names = Names(size) });
        return definition;
    }

    private Task<int> CategoryOfAsync(int productId) =>
        _api.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.Products.Where(p => p.Id == productId).Select(p => p.CategoryId)));

    private Task<int> DefaultVariantAsync(int productId) =>
        _api.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.Products.Where(p => p.Id == productId).SelectMany(p => p.Variants).Where(v => v.IsDefault).Select(v => v.Id)));

    private static async Task<StorefrontProductBody> StorefrontAsync(HttpClient client, int productId)
    {
        var response = await client.GetAsync($"/api/products/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StorefrontProductBody>(TestApi.Json))!;
    }

    private static async Task<List<int>> ListIdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>(url, TestApi.Json))!.Items.Select(i => i.Id).ToList();

    private static async Task<List<WishlistItemBody>> WishlistAsync(HttpClient customer) =>
        (await customer.GetFromJsonAsync<WishlistBody>("/api/wishlist", TestApi.Json))!.Items;

    private static async Task<TestApi.BasketBody> AddAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/basket/items", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.BasketBody>(TestApi.Json))!;
    }

    private sealed record Shirt(int ProductId, int SmallRed, int SmallBlue, int LargeRed, AdminProductBody Product);

    private sealed record StorefrontProductBody(
        int Id, string Slug, string Name, decimal Price, decimal? CompareAtPrice, string Currency, int StockQuantity,
        bool PriceIsFrom, List<StorefrontOptionBody>? Options, List<StorefrontVariantBody>? Variants);

    private sealed record StorefrontOptionBody(int Id, Dictionary<string, string> Names, List<StorefrontValueBody> Values);
    private sealed record StorefrontValueBody(int Id, Dictionary<string, string> Names);
    private sealed record StorefrontVariantBody(int Id, List<int> OptionValueIds, decimal Price, decimal? CompareAtPrice, int Available);

    private sealed record OrderBody(List<OrderItemBody> Items);
    private sealed record OrderItemBody(int ProductId, int VariantId, int Quantity, string? VariantLabel, string? Sku);
    private sealed record WishlistBody(List<WishlistItemBody> Items);
    private sealed record WishlistItemBody(int Id, decimal Price, int StockQuantity);
}
