using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// محرّك البحث المحلي (M3، ADR-0042) عبر HTTP وSQL Server الحقيقيين — وهو الموضع الوحيد الذي يثبت أنّ التطبيع
// المخزَّن والفهرس والترتيب تعمل معاً فعلاً: اختبار مجال يثبت الدالّة، وهذا يثبت أنّ ما في القاعدة يطابقها.
//
// كل اختبار يعزل بياناته في فئة جديدة فريدة ويصفّي بها (categoryIds=…)، لأنّ القاعدة مشتركة على مدار التشغيل.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CatalogSearchTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public CatalogSearchTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الاستعلام_بلا_تشكيل_وبهاء_يجد_اسماً_مشكَّلاً_بتاء_مربوطة()
    {
        // جوهر M3: التاجر كتب الاسم مشكَّلاً بتاء مربوطة، والمتسوّق يكتبه كما يكتبه الناس. قبل M3 لم يتطابقا إطلاقاً.
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var id = await CreateAsync(admin, category, "مَكْنَسَة كَهْرَبَائِيَّة");

        (await SearchAsync(category, "مكنسه كهربائيه")).Should().Equal(new[] { id }, "التطبيع يطوي التشكيل والتاء المربوطة");
        (await SearchAsync(category, "مكنسة كهربائية")).Should().Equal(id);
        (await SearchAsync(category, "مَكْنَسَة")).Should().Equal(id);
        (await SearchAsync(category, "مكــنسة")).Should().Equal(new[] { id }, "الكشيدة زخرفة لا تمنع مطابقة");
    }

    [Fact]
    public async Task صور_الألف_والألف_المقصورة_تُطوى()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var lamp = await CreateAsync(admin, category, "إضاءة مستوى أعلى");

        (await SearchAsync(category, "اضاءه مستوي اعلي")).Should().Equal(lamp);
        (await SearchAsync(category, "إضاءة")).Should().Equal(lamp);
    }

    [Fact]
    public async Task كل_كلمة_شرط_مستقلّ_فالكلمات_المتفرّقة_تُطابق()
    {
        // قبل M3 كان الاستعلام سلسلة واحدة متّصلة: "مكنسة سامسونج" لا تجد "مكنسة كهربائية سامسونج" إطلاقاً.
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var id = await CreateAsync(admin, category, "مكنسة كهربائية سامسونج");

        (await SearchAsync(category, "مكنسة سامسونج")).Should().Equal(id);
        (await SearchAsync(category, "سامسونج مكنسة")).Should().Equal(new[] { id }, "الترتيب لا يهمّ: كل كلمة شرط");
        (await SearchAsync(category, "مكنسة سامسونج جدار")).Should().BeEmpty("كلمة غير موجودة تُسقط النتيجة");
    }

    [Fact]
    public async Task الأرقام_العربية_واللاتينية_استعلام_واحد()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var id = await CreateAsync(admin, category, "مكيف ١٢٠٠٠ وحدة");

        (await SearchAsync(category, "مكيف 12000")).Should().Equal(id);
        (await SearchAsync(category, "١٢٠٠٠")).Should().Equal(id);
    }

    [Fact]
    public async Task اسم_الفئة_مسار_مطابقة_أيضاً()
    {
        // إضافة M3: من يكتب اسم فئة يريد ما فيها، لا نتيجة فارغة.
        var admin = await _api.AdminAsync();
        var slug = $"it-{Guid.NewGuid():N}"[..24];
        var category = await CreateCategoryNamedAsync(admin, slug, "أَجْهِزَة مَنْزِلِيَّة");
        var id = await CreateAsync(admin, category, "غسالة أطباق");

        (await SearchAsync(category, "اجهزه منزليه")).Should().Equal(new[] { id }, "الفئة تُطابَق مطبَّعةً كالمنتج");
        (await SearchAsync(category, "غسالة")).Should().Equal(id);
    }

    [Fact]
    public async Task ترتيب_المطابقة_يقدّم_التطابق_التامّ_ثم_البادئة_ثم_الاحتواء_ثم_الوصف()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);

        // تُنشأ بترتيب مقصود مخالف لترتيب المطابقة المتوقّع، كي لا يمرّ الاختبار بترتيب الإدخال أو بالمعرّف.
        var byDescription = await CreateAsync(admin, category, "جهاز تنظيف", description: "يُستخدم مع مكنسة المنزل");
        var contains = await CreateAsync(admin, category, "أفضل مكنسة للسيارة");
        var exact = await CreateAsync(admin, category, "مكنسة");
        var prefix = await CreateAsync(admin, category, "مكنسة كهربائية صغيرة");

        (await SearchAsync(category, "مكنسة", sortBy: "Relevance"))
            .Should().Equal([exact, prefix, contains, byDescription],
                "التطابق التامّ (100) ثم البادئة (90) ثم الاحتواء (80) ثم ما جاء من الوصف (20)");
    }

    [Fact]
    public async Task ترتيب_المطابقة_بلا_كلمة_بحث_يعود_إلى_الأحدث()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var first = await CreateAsync(admin, category, "منتج أول");
        var second = await CreateAsync(admin, category, "منتج ثانٍ");

        (await SearchAsync(category, keyword: null, sortBy: "Relevance"))
            .Should().Equal([second, first], "بلا كلمة بحث لا درجة مطابقة — الأحدث أولاً، لا ترتيب عشوائي ولا خطأ");
    }

    [Fact]
    public async Task البحث_لا_يكشف_مسودّة_ولا_مؤرشفاً_ولا_منتج_فئة_معطّلة()
    {
        // التطبيع وسّع المطابقة، فيجب أن يبقى ما هو مخفيّ مخفيّاً: البحث يمرّ بـ VisibleProducts نفسها.
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var visible = await CreateAsync(admin, category, "مكنسة ظاهرة");
        var draft = await CreateAsync(admin, category, "مكنسة مسودّة");
        var archived = await CreateAsync(admin, category, "مكنسة مؤرشفة");
        await SetStatusAsync(admin, draft, "Draft");
        await SetStatusAsync(admin, archived, "Archived");

        (await SearchAsync(category, "مكنسه")).Should().Equal(visible);

        // فئة معطّلة تخفي منتجها كذلك.
        var hiddenCategory = await _api.CreateCategoryAsync(admin);
        var inHidden = await CreateAsync(admin, hiddenCategory, "مكنسة في فئة معطّلة");
        (await SearchAsync(hiddenCategory, "مكنسه")).Should().Equal(inHidden);
        var hide = await admin.PutAsJsonAsync($"/api/categories/{hiddenCategory}", new
        {
            slug = await AdminCategorySlugAsync(admin, hiddenCategory),
            translations = new Dictionary<string, object> { ["ar"] = new { name = "فئة معطّلة" } },
            parentId = (int?)null, sortOrder = 0, isActive = false,
        });
        hide.IsSuccessStatusCode.Should().BeTrue(await hide.Content.ReadAsStringAsync());
        (await SearchAsync(hiddenCategory, "مكنسه")).Should().BeEmpty("فئة معطّلة تخفي منتجاتها من البحث كما من القائمة");
    }

    [Fact]
    public async Task بحث_الإدارة_يطبّع_الاسم_ويبقي_المعرّف_وSKU_كما_هما()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var token = $"{Guid.NewGuid():N}"[..8];
        var id = await CreateAsync(admin, category, "مِكْوَاة بُخَار", sku: $"st-{token}", slug: $"steam-{token}");

        (await AdminSearchAsync(admin, category, "مكواه بخار")).Should().Equal(new[] { id }, "التاجر يجد ما كتبه هو، مشكَّلاً أو لا");
        (await AdminSearchAsync(admin, category, $"steam-{token}")).Should().Equal(new[] { id }, "المعرّف النصّي يُطابَق كما هو");
        (await AdminSearchAsync(admin, category, $"ST-{token}")).Should().Equal(new[] { id }, "SKU يُطابَق كما هو");
    }

    [Fact]
    public async Task التعديل_يُحدِّث_ما_يُطابقه_البحث()
    {
        // الدعوى التي يحرسها: الصورة المطبَّعة مشتقّة، فلا تتقادم بعد تعديل — وإلا وُجد المنتج باسمه القديم وحده.
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var id = await CreateAsync(admin, category, "مكنسة قديمة", slug: $"old-{Guid.NewGuid():N}"[..20]);
        var slug = (await AdminProductSlugAsync(admin, id))!;

        (await admin.PutAsJsonAsync($"/api/products/{id}",
                TestApi.ProductUpdateBody(category, slug, 10m, "غَسَّالَة جَدِيدَة")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SearchAsync(category, "غساله جديده")).Should().Equal(new[] { id }, "الاسم الجديد يُطابَق مطبَّعاً");
        (await SearchAsync(category, "مكنسه")).Should().BeEmpty("والاسم القديم لم يبقَ مفهرساً");
    }

    [Fact]
    public async Task عزل_المتاجر_قائم_على_البحث_المطبَّع()
    {
        // متجران يسمّيان منتجيهما الاسم نفسه: كلٌّ يجد منتجه هو فقط. الفهرس الجديد يبدأ بالمستأجر لهذا السبب.
        const string sharedName = "مَكْنَسَة مشتركة";
        var storeB = _api.ForStore(await _factory.CreateStoreAsync());

        var adminA = await _api.AdminAsync();
        var adminB = await storeB.AdminAsync();
        var categoryA = await _api.CreateCategoryAsync(adminA);
        var categoryB = await storeB.CreateCategoryAsync(adminB);
        var productA = await CreateAsync(adminA, categoryA, sharedName);
        var productB = await CreateAsync(adminB, categoryB, sharedName);

        (await SearchAsync(_api.Anonymous(), categoryA, "مكنسه مشتركه")).Should().Equal(productA);
        (await SearchAsync(storeB.Anonymous(), categoryB, "مكنسه مشتركه")).Should().Equal(productB);
    }

    // ── أدوات ──────────────────────────────────────────────────────────────────

    private async Task<List<int>> SearchAsync(int category, string? keyword, string? sortBy = null) =>
        await SearchAsync(_api.Anonymous(), category, keyword, sortBy);

    private static async Task<List<int>> SearchAsync(
        HttpClient client, int category, string? keyword, string? sortBy = null)
    {
        var query = $"categoryIds={category}&pageSize=50";
        if (keyword is not null) query += $"&keyword={Uri.EscapeDataString(keyword)}";
        if (sortBy is not null) query += $"&sortBy={sortBy}";
        return (await client.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>($"/api/products?{query}", TestApi.Json))!
            .Items.Select(i => i.Id).ToList();
    }

    private static async Task<List<int>> AdminSearchAsync(HttpClient admin, int category, string keyword) =>
        (await admin.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>(
            $"/api/admin/products?categoryId={category}&keyword={Uri.EscapeDataString(keyword)}&pageSize=50", TestApi.Json))!
            .Items.Select(i => i.Id).ToList();

    private static async Task<int> CreateAsync(
        HttpClient admin, int categoryId, string name, string? description = null, string? sku = null, string? slug = null)
    {
        var body = new
        {
            categoryId,
            translations = new Dictionary<string, object> { ["ar"] = new { name, description } },
            price = 10m,
            stockQuantity = 5,
            slug,
            sku,
        };
        var response = await admin.PostAsJsonAsync("/api/products", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
    }

    private static async Task<int> CreateCategoryNamedAsync(HttpClient admin, string slug, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/categories",
            new { slug, translations = new Dictionary<string, object> { ["ar"] = new { name } } });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
    }

    private static async Task SetStatusAsync(HttpClient admin, int id, string status) =>
        (await admin.PutAsJsonAsync($"/api/admin/products/{id}/status", new { status }))
            .IsSuccessStatusCode.Should().BeTrue();

    private static async Task<string?> AdminProductSlugAsync(HttpClient admin, int id) =>
        (await admin.GetFromJsonAsync<SlugBody>($"/api/admin/products/{id}", TestApi.Json))!.Slug;

    private static async Task<string> AdminCategorySlugAsync(HttpClient admin, int id) =>
        (await admin.GetFromJsonAsync<List<CategorySlugBody>>("/api/admin/categories", TestApi.Json))!
            .Single(c => c.Id == id).Slug;

    private sealed record SlugBody(int Id, string Slug);
    private sealed record CategorySlugBody(int Id, string Slug);
}
