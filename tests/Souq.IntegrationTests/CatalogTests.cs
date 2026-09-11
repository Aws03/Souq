using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الكتالوج (المرحلة 5) عبر HTTP وSQL Server الحقيقيين: دورة حياة المنتج بين الإدارة والمتجر، النصوص لكل لغة ذهاباً
// وإياباً، المعرّف النصّي، سعر المقارنة وفلتر العروض، شجرة الفئات (حلقة/عمق/إخفاء)، معرض الصور، وعدد استعلامات
// ثابت للقوائم (لا N+1). كل اختبار يعزل بياناته في فئة جديدة فريدة.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CatalogTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];

    private readonly TestApi _api;

    public CatalogTests(SouqApiFactory factory) => _api = new TestApi(factory);

    [Fact]
    public async Task الإدارة_ترى_كل_الحالات_والمتجر_يرى_المنشور_فقط()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var active = await _api.CreateProductAsync(admin, categoryId: category);
        var draft = await _api.CreateProductAsync(admin, categoryId: category);
        var archived = await _api.CreateProductAsync(admin, categoryId: category);
        await SetStatusAsync(admin, draft, "Draft");
        await SetStatusAsync(admin, archived, "Archived");

        (await AdminListAsync(admin, $"categoryId={category}"))
            .Should().BeEquivalentTo(new[] { (active, "Active"), (draft, "Draft"), (archived, "Archived") });
        (await AdminListAsync(admin, $"categoryId={category}&status=Draft")).Should().Equal((draft, "Draft"));
        (await StoreIdsAsync($"categoryIds={category}")).Should().Equal(active);
        (await _api.Anonymous().GetAsync($"/api/products/{draft}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AdminProductAsync(admin, draft)).Status.Should().Be("Draft");

        // نشر المسودّة، واستعادة المؤرشف مسودّةً (مخفيّاً حتى يُنشر).
        await SetStatusAsync(admin, draft, "Active");
        await SetStatusAsync(admin, archived, "Draft");
        (await StoreIdsAsync($"categoryIds={category}")).Should().BeEquivalentTo(new[] { active, draft });
        (await AdminListAsync(admin, $"categoryId={category}&status=Archived")).Should().BeEmpty();
    }

    [Fact]
    public async Task النصوص_لكل_لغة_تُحفظ_وتُعاد_والمتجر_يعرض_لغته_الافتراضية()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var token = $"{Guid.NewGuid():N}"[..10];
        var id = await CreateAsync(admin, new
        {
            categoryId = category,
            translations = new Dictionary<string, object>
            {
                ["ar"] = new { name = "فأرة لاسلكية", description = "وصف عربي", metaTitle = "فأرة", metaDescription = "وصف SEO" },
                ["en"] = new { name = $"Wireless Mouse {token}", description = "English description" },
            },
            price = 20m, stockQuantity = 3, sku = $"ms-{token}", brand = " Logi ",
        });

        var edit = await AdminProductAsync(admin, id);
        edit.Translations.Keys.Should().BeEquivalentTo(new[] { "ar", "en" });
        edit.Translations["ar"].Should().Be(new TextBody("فأرة لاسلكية", "وصف عربي", "فأرة", "وصف SEO"));
        edit.Translations["en"].Should().Be(new TextBody($"Wireless Mouse {token}", "English description", null, null));
        edit.Sku.Should().Be($"MS-{token}".ToUpperInvariant());
        edit.Brand.Should().Be("Logi");
        edit.Slug.Should().Be($"wireless-mouse-{token}", "المعرّف يُقترح من الاسم اللاتيني حين لا يُرسل");

        var store = await _api.Anonymous().GetFromJsonAsync<StoreProductBody>($"/api/products/{id}", TestApi.Json);
        store!.Name.Should().Be("فأرة لاسلكية", "الاسم العام بلغة المتجر الافتراضية");
        store.Description.Should().Be("وصف عربي");
        store.Translations["en"].Name.Should().Be($"Wireless Mouse {token}");
        (await StoreIdsAsync($"categoryIds={category}&keyword={token}")).Should().Equal(new[] { id }, "البحث يشمل نصوص كل اللغات");

        // الاستبدال كامل: لغة لم تعد مُدخلة تُحذف.
        (await admin.PutAsJsonAsync($"/api/products/{id}", TestApi.ProductUpdateBody(category, edit.Slug, 20m, "فأرة")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await AdminProductAsync(admin, id);
        after.Translations.Keys.Should().Equal("ar");
        after.Translations["ar"].Name.Should().Be("فأرة");
    }

    [Fact]
    public async Task المعرّف_النصّي_يفتح_المنشور_فقط_ويُطبَّع_والصيغة_الخاطئة_تُرفض()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var slug = $"slug-{Guid.NewGuid():N}"[..24];
        var id = await CreateAsync(admin, TestApi.ProductBody(category, slug: slug));

        (await _api.Anonymous().GetFromJsonAsync<TestApi.IdBody>($"/api/products/by-slug/{slug}", TestApi.Json))!.Id.Should().Be(id);
        (await _api.Anonymous().GetFromJsonAsync<TestApi.IdBody>($"/api/products/by-slug/{slug.ToUpperInvariant()}", TestApi.Json))!
            .Id.Should().Be(id);

        await SetStatusAsync(admin, id, "Archived");
        (await _api.Anonymous().GetAsync($"/api/products/by-slug/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invalid = await admin.PostAsJsonAsync("/api/products", TestApi.ProductBody(category, slug: "Not A Slug"));
        (await ProblemAsync(invalid)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidProductData"));
    }

    [Fact]
    public async Task سعر_المقارنة_يضع_المنتج_في_العروض_ولا_يُقبل_إلا_أعلى_من_السعر()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var onSale = await CreateAsync(admin, new
        {
            categoryId = category, translations = Arabic("عرض"), price = 8m, compareAtPrice = 10m, stockQuantity = 2,
        });
        var regular = await _api.CreateProductAsync(admin, price: 8m, categoryId: category);

        (await StoreIdsAsync($"categoryIds={category}&onSale=true")).Should().Equal(onSale);
        (await StoreIdsAsync($"categoryIds={category}")).Should().BeEquivalentTo(new[] { onSale, regular });
        var product = await _api.Anonymous().GetFromJsonAsync<StoreProductBody>($"/api/products/{onSale}", TestApi.Json);
        (product!.Price, product.CompareAtPrice).Should().Be((8m, (decimal?)10m));

        // "خصم" لا يعلو السعر يُرفض عند الحدّ (تحقّق شكلي 400 قبل المعالج؛ الكيان يحرسه أيضاً) ولا يُنشأ شيء.
        var notHigher = await admin.PostAsJsonAsync("/api/products", new
        {
            categoryId = category, translations = Arabic("خصم وهمي"), price = 8m, compareAtPrice = 8m, stockQuantity = 1,
        });
        (await ProblemAsync(notHigher)).Should().Be((HttpStatusCode.BadRequest, "ValidationFailed"));
        (await AdminListAsync(admin, $"categoryId={category}")).Should().HaveCount(2);
    }

    [Fact]
    public async Task شجرة_الفئات_ترفض_الحلقة_وتجاوز_العمق()
    {
        var admin = await _api.AdminAsync();
        var rootSlug = $"tree-{Guid.NewGuid():N}"[..20];
        var root = await _api.CreateCategoryAsync(admin, rootSlug);
        var child = await _api.CreateCategoryAsync(admin, parentId: root);
        var grandchild = await _api.CreateCategoryAsync(admin, parentId: child);

        foreach (var parent in new[] { grandchild, root })
        {
            var cycle = await admin.PutAsJsonAsync($"/api/categories/{root}", TestApi.CategoryBody(rootSlug, "جذر", parentId: parent));
            (await ProblemAsync(cycle)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidParent"));
        }
        (await AdminCategoriesAsync(admin)).Single(c => c.Id == root).ParentId.Should().BeNull();

        // جذر + 4 مستويات = 5 (الحدّ)، والسادس مرفوض.
        var fourth = await _api.CreateCategoryAsync(admin, parentId: grandchild);
        var fifth = await _api.CreateCategoryAsync(admin, parentId: fourth);
        var tooDeep = await admin.PostAsJsonAsync("/api/categories", TestApi.CategoryBody(parentId: fifth));
        (await ProblemAsync(tooDeep)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidParent"));
    }

    [Fact]
    public async Task الفئة_المخفية_تُخفي_منتجاتها_من_المتجر_وتبقى_للإدارة()
    {
        var admin = await _api.AdminAsync();
        var slug = $"hide-{Guid.NewGuid():N}"[..20];
        var category = await _api.CreateCategoryAsync(admin, slug);
        var product = await _api.CreateProductAsync(admin, categoryId: category);

        var hide = await admin.PutAsJsonAsync($"/api/categories/{category}", new
        {
            slug, translations = Arabic("مخفية"), parentId = (int?)null, sortOrder = 3, isActive = false,
        });
        hide.IsSuccessStatusCode.Should().BeTrue(await hide.Content.ReadAsStringAsync());

        (await StoreIdsAsync($"categoryIds={category}")).Should().BeEmpty();
        (await _api.Anonymous().GetAsync($"/api/products/{product}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.Anonymous().GetFromJsonAsync<List<TestApi.IdBody>>("/api/categories", TestApi.Json))!
            .Select(c => c.Id).Should().NotContain(category);

        var hidden = (await AdminCategoriesAsync(admin)).Single(c => c.Id == category);
        (hidden.IsActive, hidden.SortOrder, hidden.Name).Should().Be((false, 3, "مخفية"));
        (await AdminListAsync(admin, $"categoryId={category}")).Should().Equal(new[] { (product, "Active") }, "المنتج نفسه لم يُمسّ");
    }

    [Fact]
    public async Task معرض_الصور_يُرتَّب_وتُزال_صوره_والرئيسية_أولاها()
    {
        var admin = await _api.AdminAsync();
        var id = await _api.CreateProductAsync(admin);
        var first = await UploadAsync(admin, id);
        var second = await UploadAsync(admin, id);
        (first.SortOrder, second.SortOrder).Should().Be((0, 1));

        var reorder = await admin.PutAsJsonAsync($"/api/admin/products/{id}/images/order", new { imageIds = new[] { second.Id, first.Id } });
        reorder.IsSuccessStatusCode.Should().BeTrue(await reorder.Content.ReadAsStringAsync());
        (await AdminProductAsync(admin, id)).Images.Select(i => i.Id).Should().Equal(second.Id, first.Id);
        var store = await _api.Anonymous().GetFromJsonAsync<StoreProductBody>($"/api/products/{id}", TestApi.Json);
        store!.ImageUrl.Should().Be(second.ImageUrl);
        store.Images.Should().Equal(second.ImageUrl, first.ImageUrl);

        // الترتيب يشمل كل الصور مرّة واحدة — ناقص ⇒ 422 بلا تغيير.
        var partial = await admin.PutAsJsonAsync($"/api/admin/products/{id}/images/order", new { imageIds = new[] { second.Id } });
        (await ProblemAsync(partial)).Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidProductData"));

        (await admin.DeleteAsync($"/api/admin/products/{id}/images/{second.Id}")).IsSuccessStatusCode.Should().BeTrue();
        (await AdminProductAsync(admin, id)).Images.Select(i => i.Id).Should().Equal(first.Id);
        (await admin.DeleteAsync($"/api/admin/products/{id}/images/{second.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task قوائم_الكتالوج_بعدد_استعلامات_ثابت_لا_لكل_منتج()
    {
        var admin = await _api.AdminAsync();
        var small = await _api.CreateCategoryAsync(admin);
        var large = await _api.CreateCategoryAsync(admin);
        for (var i = 0; i < 2; i++) await UploadAsync(admin, await _api.CreateProductAsync(admin, categoryId: small));
        for (var i = 0; i < 10; i++) await UploadAsync(admin, await _api.CreateProductAsync(admin, categoryId: large));
        var store = _api.Anonymous();

        var storeFew = await CountProductCommandsAsync(() => store.GetAsync($"/api/products?categoryIds={small}&pageSize=50"));
        var storeMany = await CountProductCommandsAsync(() => store.GetAsync($"/api/products?categoryIds={large}&pageSize=50"));
        var adminFew = await CountProductCommandsAsync(() => admin.GetAsync($"/api/admin/products?categoryId={small}&pageSize=50"));
        var adminMany = await CountProductCommandsAsync(() => admin.GetAsync($"/api/admin/products?categoryId={large}&pageSize=50"));

        storeMany.Should().Be(storeFew, "قائمة المتجر لا تزيد استعلاماتها بعدد المنتجات").And.BeInRange(1, 4);
        adminMany.Should().Be(adminFew, "جدول الإدارة لا يزيد استعلاماته بعدد المنتجات").And.BeInRange(1, 4);
    }

    // ── أدوات ──────────────────────────────────────────────────────────────────

    private static Dictionary<string, object> Arabic(string name) => new() { ["ar"] = new { name } };

    private static async Task<int> CreateAsync(HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync("/api/products", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
    }

    private static async Task SetStatusAsync(HttpClient admin, int id, string status)
    {
        var response = await admin.PutAsJsonAsync($"/api/admin/products/{id}/status", new { status });
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    private static async Task<UploadBody> UploadAsync(HttpClient admin, int productId)
    {
        var file = new ByteArrayContent(PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await admin.PostAsync($"/api/products/{productId}/image", new MultipartFormDataContent { { file, "file", "p.png" } });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<UploadBody>(TestApi.Json))!;
    }

    private async Task<List<int>> StoreIdsAsync(string query) =>
        (await _api.Anonymous().GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>($"/api/products?{query}&pageSize=50", TestApi.Json))!
            .Items.Select(i => i.Id).ToList();

    private static async Task<List<(int Id, string Status)>> AdminListAsync(HttpClient admin, string query) =>
        (await admin.GetFromJsonAsync<TestApi.PageBody<AdminListItem>>($"/api/admin/products?{query}&pageSize=50", TestApi.Json))!
            .Items.Select(i => (i.Id, i.Status)).ToList();

    private static async Task<AdminProductBody> AdminProductAsync(HttpClient admin, int id) =>
        (await admin.GetFromJsonAsync<AdminProductBody>($"/api/admin/products/{id}", TestApi.Json))!;

    private static async Task<List<CategoryBody>> AdminCategoriesAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<List<CategoryBody>>("/api/admin/categories", TestApi.Json))!;

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private static async Task<int> CountProductCommandsAsync(Func<Task<HttpResponseMessage>> request)
    {
        using var counter = new ProductCommandCounter();
        (await request()).StatusCode.Should().Be(HttpStatusCode.OK);
        return counter.Count;
    }

    // عدّاد أوامر SQL التي تمسّ جدول المنتجات أثناء طلب واحد — عبر DiagnosticListener لـ EF Core في العملية نفسها
    // (الخادم في الذاكرة، واختبارات التكامل متتابعة في مجموعة واحدة فلا ضجيج من طلبات أخرى).
    private sealed class ProductCommandCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private int _count;

        public ProductCommandCounter() => _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));

        public int Count => Volatile.Read(ref _count);

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name) return;
            lock (_subscriptions) _subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> e)
        {
            if (e.Key == RelationalEventId.CommandExecuted.Name && e.Value is CommandExecutedEventData data
                && data.Command.CommandText.Contains("[Products]", StringComparison.Ordinal))
                Interlocked.Increment(ref _count);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_subscriptions)
                foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }

    private sealed record TextBody(string Name, string? Description, string? MetaTitle, string? MetaDescription);
    private sealed record ImageBody(int Id, string Url, int SortOrder);
    private sealed record UploadBody(int Id, string ImageUrl, int SortOrder);
    private sealed record AdminListItem(int Id, string Status);
    private sealed record AdminProductBody(
        int Id, string Slug, string Status, Dictionary<string, TextBody> Translations, string? Sku, decimal Price,
        decimal? CompareAtPrice, List<ImageBody> Images, string? Brand);
    private sealed record StoreProductBody(
        int Id, string Slug, string Name, string? Description, Dictionary<string, TextBody> Translations,
        decimal Price, decimal? CompareAtPrice, string? ImageUrl, List<string>? Images);
    private sealed record CategoryBody(int Id, string Name, int? ParentId, int SortOrder, bool IsActive);
}
