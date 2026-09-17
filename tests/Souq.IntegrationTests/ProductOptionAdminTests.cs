using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.Entities;
using Souq.IntegrationTests.Infrastructure;
using static Souq.IntegrationTests.Infrastructure.VariantAdminApi;

namespace Souq.IntegrationTests;

// ============================================================================
// الخيارات والمتغيّرات في الإدارة (ProductVariants.md V2، ADR-0040) عبر HTTP وSQL Server الحقيقيين: تحويل منتج بسيط إلى
// منتج بخيارين، توليد التركيبات بمخزون كلٍّ منها، التسعير وSKU لكل متغيّر، التفعيل والتعطيل والافتراضي، الحذف الآمن للقيم،
// لقطة الوصف في الطلب، قيود القاعدة، التزامن، بوّابة واجهة المتجر المؤقّتة، وإشعار المخزون بوصف المتغيّر.
// عزل المتاجر لهذه النقاط في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ProductOptionAdminTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public ProductOptionAdminTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task تاجر_يحوّل_منتجاً_بسيطاً_إلى_مقاسين_ولونين_ويدير_متغيّراته_ومخزونها()
    {
        var admin = await _api.AdminAsync();
        var sku = $"TEE{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(await _api.CreateCategoryAsync(admin), 20m, 6, sku: sku));
        var productId = (await created.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;

        // منتج بسيط: لا خيارات، متغيّر افتراضي واحد بمخزونه، والحدود منشورة من الخادم.
        var simple = await ProductAsync(admin, productId);
        simple.Options.Should().BeEmpty();
        simple.Variants.Should().ContainSingle().Which.Should().BeEquivalentTo(new { IsDefault = true, IsActive = true, Sku = sku, OnHand = 6 });
        simple.VariantLimits.Should().Be(new LimitsBody(3, 20, 100, 50, 64));
        var original = simple.Variants.Single().Id;

        // خيار أول: المتغيّر القائم يصير "M" بهويته ومخزونه؛ ثم خيار ثانٍ يأخذ فيه "أحمر".
        (await SetOptionsAsync(admin, productId, [Option("المقاس", ["S", "M", "L"], existing: 1, en: "Size")])).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var sized = await ProductAsync(admin, productId);
        (await SetOptionsAsync(admin, productId, [.. Current(sized), Option("اللون", ["أحمر", "أزرق"], existing: 0, en: "Colour")]))
            .EnsureSuccessStatusCode();
        var product = await ProductAsync(admin, productId);
        product.Options.Select(o => o.Names["ar"]).Should().Equal("المقاس", "اللون");
        product.Variants.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = original, OnHand = 6,
            OptionValueIds = new[] { ValueId(product, "المقاس", "M"), ValueId(product, "اللون", "أحمر") },
        });

        // التركيبات: متغيّران نشطان ومتغيّر معطّل، بمخزون كلٍّ منها يُفتح في Inventory.
        var ids = await CreateVariantsOkAsync(admin, productId,
            new { optionValueIds = new[] { ValueId(product, "المقاس", "L"), ValueId(product, "اللون", "أحمر") }, price = 22m, sku = $"{sku}-LR", initialStock = 3 },
            new { optionValueIds = new[] { ValueId(product, "اللون", "أزرق"), ValueId(product, "المقاس", "S") }, price = 19.5m, compareAtPrice = 24m, initialStock = 0, lowStockThreshold = 1 },
            new { optionValueIds = new[] { ValueId(product, "المقاس", "L"), ValueId(product, "اللون", "أزرق") }, price = 22m, isActive = false });
        ids.Should().HaveCount(3).And.OnlyHaveUniqueItems();

        product = await ProductAsync(admin, productId);
        product.Variants.Select(v => (v.Price, v.IsActive, v.OnHand, v.LowStockThreshold)).Should().Equal(
            (19.5m, true, 0, 1),     // S / أزرق
            (20m, true, 6, 5),       // M / أحمر (الأصلي)
            (22m, true, 3, 5),       // L / أحمر
            (22m, false, 0, 5));     // L / أزرق
        product.OnHand.Should().Be(9, "مخزون المنتج مجموع متغيّراته");

        var inventory = await InventoryRowsAsync(admin, productId);
        inventory.Select(r => (r.VariantId, r.VariantLabel, r.VariantIsActive)).Should().BeEquivalentTo(new[]
        {
            (original, (string?)"M / أحمر", true), (ids[0], "L / أحمر", true), (ids[1], "S / أزرق", true), (ids[2], "L / أزرق", false),
        });

        // تعديل متغيّر، تعطيله وتفعيله، ونقل الافتراضي — ثم لا يُعطَّل الافتراضي.
        (await admin.PutAsJsonAsync($"/api/admin/products/{productId}/variants/{ids[0]}", new { price = 23.5m, sku = $"{sku}-lr2" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/api/admin/products/{productId}/variants/{ids[2]}/status", new { isActive = true })).EnsureSuccessStatusCode();
        (await ProblemAsync(await admin.PutAsJsonAsync($"/api/admin/products/{productId}/variants/{original}/status", new { isActive = false })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "DefaultVariantCannotBeDeactivated"));
        (await admin.PutAsync($"/api/admin/products/{productId}/variants/{ids[0]}/default", null)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/products/{productId}/variants/{original}/status", new { isActive = false })).EnsureSuccessStatusCode();

        // بعد إعادة التحميل: كل شيء محفوظ كما أُرسل، وسعر المنتج في القوائم سعر الافتراضي الجديد.
        product = await ProductAsync(admin, productId);
        product.Variants.Single(v => v.Id == ids[0]).Should().BeEquivalentTo(new { IsDefault = true, Price = 23.5m, Sku = $"{sku}-LR2" });
        product.Variants.Single(v => v.Id == original).Should().BeEquivalentTo(new { IsDefault = false, IsActive = false });
        (product.Price, product.Sku).Should().Be((23.5m, $"{sku}-LR2"));
        product.Variants.Count(v => v.IsDefault).Should().Be(1);

        // نموذج المنتج القديم لا يغيّر سعر منتج بخيارات، ويعدّل الاسم إن أعاد السعر كما قرأه.
        (await ProblemAsync(await admin.PutAsJsonAsync($"/api/products/{productId}", new
            {
                categoryId = await CategoryOfAsync(productId), translations = new Dictionary<string, object> { ["ar"] = new { name = "قميص" } },
                price = 99m, slug = product.Slug, sku = product.Sku,
            })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "ProductHasVariants"));
        (await admin.PutAsJsonAsync($"/api/products/{productId}", new
            {
                categoryId = await CategoryOfAsync(productId), translations = new Dictionary<string, object> { ["ar"] = new { name = "قميص قطني" } },
                price = product.Price, slug = product.Slug, sku = product.Sku,
            })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _api.WithDbAsync(db => db.AuditEntries.Where(a => a.TargetId == productId.ToString() || ids.Select(i => i.ToString()).Contains(a.TargetId!))
                .Select(a => a.Action).Distinct().ToListAsync()))
            .Should().Contain(["catalog.product.options-set", "catalog.product.variants-created", "catalog.product.variant-updated",
                "catalog.product.variant-activated", "catalog.product.default-variant-set"]);
    }

    [Fact]
    public async Task القيمة_المستخدمة_لا_تُحذف_والتركيبة_المكرّرة_والقيمة_الغريبة_تُرفض_بلا_أثر()
    {
        var admin = await _api.AdminAsync();
        var (productId, product) = await SizedProductAsync(admin, ["S", "M", "L"]);
        var other = await SizedProductAsync(admin, ["S", "M"]);
        var medium = (await CreateVariantsOkAsync(admin, productId, new { optionValueIds = new[] { ValueId(product, "المقاس", "M") }, price = 11m })).Single();
        (await admin.PutAsJsonAsync($"/api/admin/products/{productId}/variants/{medium}/status", new { isActive = false })).EnsureSuccessStatusCode();

        var definition = Current(product);
        definition[0]["values"] = ((List<object>)definition[0]["values"]!).Take(1).ToList();
        (await ProblemAsync(await SetOptionsAsync(admin, productId, definition)))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "OptionValueInUse"), "M يستخدمها متغيّر معطّل — المتغيّرات لا تُحذف");

        (await ProblemAsync(await CreateVariantsAsync(admin, productId, [new { optionValueIds = new[] { ValueId(product, "المقاس", "M") }, price = 12m }])))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "DuplicateVariantCombination"));
        (await ProblemAsync(await CreateVariantsAsync(admin, productId, [new { optionValueIds = new[] { ValueId(other.Product, "المقاس", "M") }, price = 12m }])))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "OptionValueNotFound"), "قيمة منتج آخر في المتجر نفسه لا تُستخدم هنا");
        (await ProblemAsync(await admin.PutAsJsonAsync($"/api/admin/products/{other.ProductId}/variants/{medium}", new { price = 1m })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"), "متغيّر منتج آخر لا يُعدَّل عبر منتج غيره");
        (await ProblemAsync(await SetOptionsAsync(admin, productId,
                [Option("أ", ["1"]), Option("ب", ["1"]), Option("ج", ["1"]), Option("د", ["1"])])))
            .Should().Be((HttpStatusCode.BadRequest, "ValidationFailed"), "الحدّ يُفحص شكلاً قبل المعالج أيضاً");

        // القيمة غير المستخدمة (L) تُحذف، ولا شيء آخر تغيّر.
        var withoutLarge = Current(product);
        withoutLarge[0]["values"] = ((List<object>)withoutLarge[0]["values"]!).Take(2).ToList();
        (await SetOptionsAsync(admin, productId, withoutLarge)).EnsureSuccessStatusCode();
        var after = await ProductAsync(admin, productId);
        after.Options.Single().Values.Select(v => v.Names["ar"]).Should().Equal("S", "M");
        after.Variants.Should().HaveCount(2);
        (await ProductAsync(admin, other.ProductId)).Variants.Should().ContainSingle();
    }

    [Fact]
    public async Task الطلب_يحفظ_وصف_المتغيّر_لقطةً_لا_تتبع_إعادة_التسمية()
    {
        var admin = await _api.AdminAsync();
        var (productId, product) = await SizedProductAsync(admin, ["S", "M"], stock: 5);
        var medium = (await CreateVariantsOkAsync(admin, productId, new { optionValueIds = new[] { ValueId(product, "المقاس", "M") }, price = 12m, initialStock = 5 })).Single();
        var customer = (await _api.NewCustomerAsync()).Client;

        var placed = await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = new[] { new { productId, quantity = 1, variantId = medium } },
        });
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;

        var renamed = Current(product);
        renamed[0]["values"] = new List<object>
        {
            new { id = (int?)ValueId(product, "المقاس", "S"), names = Names("S") },
            new { id = (int?)ValueId(product, "المقاس", "M"), names = Names("وسط") },
        };
        (await SetOptionsAsync(admin, productId, renamed)).EnsureSuccessStatusCode();

        (await OrderLabelsAsync(customer, orderId)).Should().Equal("M");
        (await InventoryRowsAsync(admin, productId)).Single(r => r.VariantId == medium).VariantLabel.Should().Be("وسط");
    }

    [Fact]
    public async Task واجهة_المتجر_تخفي_منتجاً_بأكثر_من_متغيّر_نشط_حتى_اختيار_المتغيّر_وتعيده_بمتغيّر_واحد()
    {
        var admin = await _api.AdminAsync();
        var (productId, product) = await SizedProductAsync(admin, ["S", "M"], stock: 4);
        var anonymous = _api.Anonymous();
        (await anonymous.GetAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.OK, "بخيار ومتغيّر واحد ما زال منتجاً بسيطاً للمتجر");

        var medium = (await CreateVariantsOkAsync(admin, productId, new { optionValueIds = new[] { ValueId(product, "المقاس", "M") }, price = 12m, initialStock = 9 })).Single();
        (await anonymous.GetAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IdsAsync(anonymous, "/api/products?pageSize=100")).Should().NotContain(productId);

        (await admin.PutAsJsonAsync($"/api/admin/products/{productId}/variants/{medium}/status", new { isActive = false })).EnsureSuccessStatusCode();
        var visible = await anonymous.GetFromJsonAsync<StorefrontProductBody>($"/api/products/{productId}", TestApi.Json);
        visible.Should().BeEquivalentTo(new { Price = 10m, StockQuantity = 4 }, "مخزون المتغيّر المعطّل لا يُعرض متاحاً للبيع");
    }

    [Fact]
    public async Task القاعدة_تحرس_تفرّد_التركيبة_ومرجع_القيمة_المستخدمة()
    {
        var admin = await _api.AdminAsync();
        var (productId, product) = await SizedProductAsync(admin, ["S", "M"]);
        var medium = (await CreateVariantsOkAsync(admin, productId, new { optionValueIds = new[] { ValueId(product, "المقاس", "M") }, price = 12m })).Single();
        var small = product.Variants.Single().Id;
        var mValue = ValueId(product, "المقاس", "M");

        // خطّ دفاع أخير تحت Product — SQL مباشر يتجاوز الكيان عمداً.
        var duplicateCombination = () => _api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [ProductVariants] SET [CombinationKey] = (SELECT [CombinationKey] FROM [ProductVariants] WHERE [Id] = {medium}) WHERE [Id] = {small}"));
        var deleteUsedValue = () => _api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [ProductOptionValueTranslations] WHERE [ProductOptionValueId] = {mValue}; DELETE FROM [ProductOptionValues] WHERE [Id] = {mValue}"));

        await duplicateCombination.Should().ThrowAsync<SqlException>();
        await deleteUsedValue.Should().ThrowAsync<SqlException>();
        (await ProductAsync(admin, productId)).Variants.Should().HaveCount(2);
    }

    [Fact]
    public async Task مديران_متزامنان_لا_يتركان_متغيّراً_بلا_قيمة_ولا_تركيبة_مكرّرة()
    {
        var admin = await _api.AdminAsync();
        var (productId, product) = await SizedProductAsync(admin, ["S", "M", "L"]);
        var large = new { optionValueIds = new[] { ValueId(product, "المقاس", "L") }, price = 12m };

        // إضافة خيار "اللون" وإنشاء متغيّر بالمقاس وحده في اللحظة نفسها، ومتغيّران بالتركيبة نفسها.
        var responses = await Task.WhenAll(
            SetOptionsAsync(admin, productId, [.. Current(product), Option("اللون", ["أحمر"], existing: 0)]),
            CreateVariantsAsync(admin, productId, [large]),
            CreateVariantsAsync(admin, productId, [large]));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.NoContent
                                            || r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        responses.Skip(1).Count(r => r.IsSuccessStatusCode).Should().BeLessThanOrEqualTo(1, "تركيبة واحدة لا تُنشأ مرّتين");

        var after = await ProductAsync(admin, productId);
        after.Variants.Should().OnlyContain(v => v.OptionValueIds.Count == after.Options.Count, "كل متغيّر يحمل قيمة من كل خيار");
        after.Variants.Select(v => string.Join(',', v.OptionValueIds.Order())).Should().OnlyHaveUniqueItems();
        (await _api.WithDbAsync(db => db.InventoryItems.CountAsync(i => i.ProductId == productId))).Should().Be(after.Variants.Count,
            "لكل متغيّر محفوظ مخزونه، ولا مخزون لمتغيّر لم يُحفظ");
    }

    [Fact]
    public async Task حارس_تزامن_الجذر_يرفض_حفظ_تعديل_بنية_قرأ_منتجاً_تغيّر_بعده()
    {
        var admin = await _api.AdminAsync();
        var (productId, product) = await SizedProductAsync(admin, ["S", "M"]);
        var medium = ValueId(product, "المقاس", "M");

        // جلستان تقرآن المنتج نفسه؛ الأولى تضيف خياراً وتحفظ، والثانية — بصورتها القديمة — تضيف متغيّراً بالمقاس وحده.
        await using var first = await _factory.TenantScopeAsync();
        await using var second = await _factory.TenantScopeAsync();
        var firstRepository = first.ServiceProvider.GetRequiredService<IProductRepository>();
        var secondRepository = second.ServiceProvider.GetRequiredService<IProductRepository>();
        var firstCopy = (await firstRepository.GetByIdAsync(productId))!;
        var staleCopy = (await secondRepository.GetByIdAsync(productId))!;

        firstCopy.SetOptions([.. firstCopy.Options.Select(o => new ProductOptionDefinition(o.Id, o.Translations.ToDictionary(t => t.Culture, t => t.Name),
                o.Values.Select(v => new ProductOptionValueDefinition(v.Id, v.Translations.ToDictionary(t => t.Culture, t => t.Name))).ToList())),
            new ProductOptionDefinition(null, Names("اللون"), [new ProductOptionValueDefinition(null, Names("أحمر"))], 0)]);
        firstRepository.GuardConcurrentEdit(firstCopy);
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        staleCopy.AddVariant([medium], new Souq.Domain.ValueObjects.Money(12m, staleCopy.Price.Currency));
        secondRepository.GuardConcurrentEdit(staleCopy);
        var staleSave = () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        await staleSave.Should().ThrowAsync<ConcurrencyConflictException>();
        var after = await ProductAsync(admin, productId);
        (after.Options.Count, after.Variants.Count).Should().Be((2, 1), "المتغيّر الناقص لم يُحفظ");
    }

    [Fact]
    public async Task إشعار_المخزون_المنخفض_يسمّي_وصف_المتغيّر()
    {
        var store = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await store.AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(await store.CreateCategoryAsync(admin), 10m, 10, name: "قميص الإشعار"));
        var productId = (await created.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        (await SetOptionsAsync(admin, productId, [Option("المقاس", ["S", "XL"], existing: 0)])).EnsureSuccessStatusCode();
        var product = await ProductAsync(admin, productId);
        var xl = (await CreateVariantsOkAsync(admin, productId, new { optionValueIds = new[] { ValueId(product, "المقاس", "XL") }, price = 12m, initialStock = 8 })).Single();
        await _factory.DispatchNotificationsAsync();

        (await admin.PostAsJsonAsync($"/api/admin/inventory/variants/{xl}/adjustments", new { delta = -5, reason = "تلف" })).EnsureSuccessStatusCode();
        await _factory.DispatchNotificationsAsync();

        var notifications = (await admin.GetFromJsonAsync<TestApi.PageBody<NotificationBody>>("/api/notifications?pageSize=50", TestApi.Json))!.Items;
        notifications.Should().ContainSingle(n => n.Kind == NotificationKinds.LowStock).Which.Data.Should().Contain(new Dictionary<string, string>
        {
            ["productName"] = "قميص الإشعار", ["variantId"] = xl.ToString(), ["variantLabel"] = "XL", ["available"] = "3",
        });
    }

    // ── أدوات ──

    // منتج بسعر 10 وخيار "المقاس"، ومتغيّره القائم على القيمة الأولى.
    private async Task<(int ProductId, AdminProductBody Product)> SizedProductAsync(HttpClient admin, string[] sizes, int stock = 5)
    {
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: stock);
        var defined = await SetOptionsAsync(admin, productId, [Option("المقاس", sizes, existing: 0)]);
        defined.StatusCode.Should().Be(HttpStatusCode.NoContent, await defined.Content.ReadAsStringAsync());
        return (productId, await ProductAsync(admin, productId));
    }

    private Task<int> CategoryOfAsync(int productId) =>
        _api.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.CategoryId).SingleAsync());

    // الجرد مرتّب بالأقلّ متاحاً في قاعدة مشتركة، فيُقرأ صفحةً صفحة حتى صفوف المنتج.
    private static async Task<List<InventoryRowBody>> InventoryRowsAsync(HttpClient admin, int productId)
    {
        var rows = new List<InventoryRowBody>();
        for (var page = 1; ; page++)
        {
            var inventory = (await admin.GetFromJsonAsync<TestApi.PageBody<InventoryRowBody>>($"/api/admin/inventory?page={page}&pageSize=100", TestApi.Json))!;
            rows.AddRange(inventory.Items.Where(r => r.Id == productId));
            if (!inventory.HasNext) return rows;
        }
    }

    private static async Task<List<string?>> OrderLabelsAsync(HttpClient customer, int orderId) =>
        (await customer.GetFromJsonAsync<OrderBody>($"/api/orders/{orderId}", TestApi.Json))!.Items.Select(i => i.VariantLabel).ToList();

    private static async Task<List<int>> IdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>(url, TestApi.Json))!.Items.Select(i => i.Id).ToList();

    private sealed record InventoryRowBody(int Id, int VariantId, string? Sku, int OnHand, string? VariantLabel, bool VariantIsActive);
    private sealed record OrderBody(List<OrderItemBody> Items);
    private sealed record OrderItemBody(int VariantId, string? VariantLabel, string? Sku);
    private sealed record StorefrontProductBody(int Id, decimal Price, int StockQuantity);
    private sealed record NotificationBody(int Id, string Kind, Dictionary<string, string> Data, bool IsRead);
}
