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

    [Fact]
    public async Task مكلسة_تسترجع_نحو_مكنسة_وتقول_ذلك_صراحةً()
    {
        // الحالة التي يسمّيها SouqMasterPlan.md M3 بالاسم، من طرف إلى طرف: خطأ مطبعي بحرف واحد، بلا أي بيانات
        // تصحيح مُدخلة — المفردات تأتي من الكتالوج نفسه، والمسافة المحدودة تفعل الباقي.
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var vacuum = await CreateAsync(admin, category, "مكنسة كهربائية");

        var page = await SearchPageAsync(category, "مكلسة");

        page.Items.Select(i => i.Id).Should().Equal(new[] { vacuum }, "التصحيح أعاد البحث ووجد المنتج");
        page.Search.Should().NotBeNull();
        page.Search!.Term.Should().Be("مكلسة", "ما كتبه المتسوّق يُعاد كما كتبه");
        page.Search.SearchedInstead.Should().Be("مكنسه", "والكلمة التي بُحث بها فعلاً تُسمّى صراحةً — لا استبدال صامت");
    }

    [Fact]
    public async Task الاسترجاع_يعمل_على_كلمة_فريدة_أيضاً_وبأنواع_الأخطاء_الشائعة()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        // كلمة لا ترد في أي اختبار آخر، فالتصحيح لا يمكن أن يأتي من بيانات غيرها.
        var id = await CreateAsync(admin, category, "جهاز زبلفون");

        // حرف ناقص من **آخر** الكلمة لا يحتاج استرجاعاً أصلاً: المطابقة احتواء، فـ"زبلفو" داخل "زبلفون".
        var truncated = await SearchPageAsync(category, "زبلفو");
        truncated.Items.Select(i => i.Id).Should().Equal(new[] { id });
        truncated.Search.Should().BeNull("الاحتواء غطّى الحالة، فلا تصحيح يُنفَّذ");

        // أمّا الخطأ داخل الكلمة فلا يغطّيه الاحتواء، وهو ما يوجد الاسترجاع لأجله:
        (await SearchPageAsync(category, "زبلون")).Search!.SearchedInstead.Should().Be("زبلفون", "حرف ناقص من الوسط");
        (await SearchPageAsync(category, "زبلفوني")).Search!.SearchedInstead.Should().Be("زبلفون", "حرف زائد");
        (await SearchPageAsync(category, "زبلفول")).Search!.SearchedInstead.Should().Be("زبلفون", "حرف مُبدَل");
        (await SearchPageAsync(category, "زبلفنو")).Search!.SearchedInstead.Should().Be("زبلفون", "جارَان مُبدَّلان");
        (await SearchAsync(category, "زبلون")).Should().Equal(new[] { id }, "والتصحيح يعيد البحث فعلاً");
    }

    [Fact]
    public async Task لا_تصحيح_حين_تُوجد_نتائج_بكلمات_المتسوّق()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        await CreateAsync(admin, category, "مكنسة كهربائية");

        var page = await SearchPageAsync(category, "مكنسة");

        page.Items.Should().NotBeEmpty();
        page.Search.Should().BeNull("وُجد ما طُلب، فلا شيء يُقال — ولا عمل استرجاع يُنفَّذ إطلاقاً");
    }

    [Fact]
    public async Task ما_هو_أبعد_من_خطأ_مطبعي_لا_يُصحَّح()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        await CreateAsync(admin, category, "جهاز زبلفون");

        // مسافة 3 أو أكثر: كلمة أخرى لا خطأ مطبعي — لا نتائج ولا تصحيح مُختلَق.
        var page = await SearchPageAsync(category, "زبلفونيات");
        page.Items.Should().BeEmpty();
        page.Search?.SearchedInstead.Should().BeNull();
    }

    [Fact]
    public async Task الاسترجاع_محدود_بعدد_الكلمات_وطولها()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        await CreateAsync(admin, category, "جهاز زبلفون");

        // أربع كلمات: فوق حدّ الاسترجاع، فلا يُحسب شيء — حدُّ كلفة على نقطة عامّة بلا حدّ معدّل.
        (await SearchPageAsync(category, "زبلفو زبلفو زبلفو زبلفو")).Search?.SearchedInstead.Should().BeNull();
        // كلمة من حرفين: تصحيحها بلا معنى.
        (await SearchPageAsync(category, "زب")).Search?.SearchedInstead.Should().BeNull();
    }

    [Fact]
    public async Task الاسترجاع_حتميّ_يعطي_الجواب_نفسه_لكل_طلب()
    {
        // يهمّ لأنّ المرشَّحات تأتي من قاموس لا ترتيب له: بلا كسر تعادل صريح لكان الجواب تابعاً لترتيب التعداد.
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        await CreateAsync(admin, category, "جهاز زبلفون");

        var answers = new List<string?>();
        for (var attempt = 0; attempt < 4; attempt++)
            answers.Add((await SearchPageAsync(category, "زبلون")).Search?.SearchedInstead);

        answers.Distinct().Should().HaveCount(1, "استعلام واحد، جواب واحد، في كل مرّة");
    }

    [Fact]
    public async Task لا_نتائج_بحال_تقترح_فئة_بدل_نهاية_مسدودة()
    {
        var admin = await _api.AdminAsync();
        var slug = $"it-{Guid.NewGuid():N}"[..24];
        var category = await CreateCategoryNamedAsync(admin, slug, "زقفونيات منزلية");
        // الفئة موجودة ومفعَّلة لكنها فارغة: البحث عن اسمها لا يعطي منتجاً، فتُقترح هي.
        var page = await SearchPageAsync(category: null, "زقفونيات");

        page.Items.Should().BeEmpty();
        page.Search.Should().NotBeNull();
        page.Search!.Category.Should().NotBeNull();
        page.Search.Category!.Slug.Should().Be(slug);
        page.Search.Category.Name.Should().Be("زقفونيات منزلية");
    }

    // ── الاقتراحات أثناء الكتابة (M3) ────────────────────────────────────────

    [Fact]
    public async Task الاقتراحات_تُعيد_منتجات_وفئات_حقيقية_مطبَّعة()
    {
        var admin = await _api.AdminAsync();
        var slug = $"it-{Guid.NewGuid():N}"[..24];
        var category = await CreateCategoryNamedAsync(admin, slug, "زمهريرات منزلية");
        var product = await CreateAsync(admin, category, "زمهرير كَهْرَبَائِيّ");

        var suggestions = await SuggestAsync("زمهرير");

        // المنتج أولاً (الوجهة الأدقّ)، ثم الفئة — والاسم يعود كما كتبه التاجر لا مطبَّعاً.
        suggestions.Should().HaveCountGreaterThanOrEqualTo(2);
        suggestions[0].Kind.Should().Be("product");
        suggestions[0].Id.Should().Be(product);
        suggestions[0].Name.Should().Be("زمهرير كَهْرَبَائِيّ", "الاسم للعرض، والصورة المطبَّعة للمطابقة وحدها");
        suggestions.Should().Contain(x => x.Kind == "category" && x.Id == category);
    }

    [Fact]
    public async Task الاقتراحات_تُطابق_مطبَّعاً_كالبحث()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var id = await CreateAsync(admin, category, "زمهريرة مُكَيَّفَة");

        (await SuggestAsync("زمهريره مكيفه")).Should().Contain(x => x.Id == id);
        (await SuggestAsync("زمهريرة")).Should().Contain(x => x.Id == id);
    }

    [Fact]
    public async Task الاقتراحات_لا_تكشف_غير_المعروض()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        var visible = await CreateAsync(admin, category, "زمهريط ظاهر");
        var draft = await CreateAsync(admin, category, "زمهريط مسودّة");
        await SetStatusAsync(admin, draft, "Draft");

        var ids = (await SuggestAsync("زمهريط")).Select(x => x.Id).ToList();

        ids.Should().Contain(visible);
        ids.Should().NotContain(draft, "الاقتراح نافذة على الكتالوج المعروض، لا على المسودّات");
    }

    [Fact]
    public async Task الاقتراحات_محدودة_عدداً_وتتجاهل_ما_هو_أقصر_من_حرفين()
    {
        var admin = await _api.AdminAsync();
        var category = await _api.CreateCategoryAsync(admin);
        for (var i = 0; i < 12; i++) await CreateAsync(admin, category, $"زمهريف رقم {i}");

        (await SuggestAsync("زمهريف")).Should().HaveCountLessThanOrEqualTo(10, "الحدّ الأعلى للاقتراحات");
        (await SuggestAsync("ز")).Should().BeEmpty("حرف واحد يطابق نصف الكتالوج: اقتراح بلا معلومة");
        (await SuggestAsync("")).Should().BeEmpty();

        // حدّ خارج المدى يُرفض بـ 400 لا يُقصّ صامتاً.
        (await _api.Anonymous().GetAsync("/api/products/suggestions?q=زمهريف&limit=50"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task الاقتراحات_معزولة_بين_المتاجر()
    {
        // نقطة بلا معامل مسار، فجدول العزل في TenantIsolationTests لا يغطّيها تلقائياً — العزل يُثبَت هنا صراحةً.
        const string sharedName = "زمهريخ مشترك";
        var storeB = _api.ForStore(await _factory.CreateStoreAsync());

        var adminA = await _api.AdminAsync();
        var adminB = await storeB.AdminAsync();
        var productA = await CreateAsync(adminA, await _api.CreateCategoryAsync(adminA), sharedName);
        var productB = await CreateAsync(adminB, await storeB.CreateCategoryAsync(adminB), sharedName);

        var fromA = (await SuggestAsync(_api.Anonymous(), "زمهريخ")).Select(x => x.Id).ToList();
        var fromB = (await SuggestAsync(storeB.Anonymous(), "زمهريخ")).Select(x => x.Id).ToList();

        fromA.Should().Contain(productA).And.NotContain(productB);
        fromB.Should().Contain(productB).And.NotContain(productA);
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

    private async Task<SearchPageBody> SearchPageAsync(int? category, string keyword)
    {
        var query = $"keyword={Uri.EscapeDataString(keyword)}&pageSize=50";
        if (category is not null) query += $"&categoryIds={category}";
        return (await _api.Anonymous().GetFromJsonAsync<SearchPageBody>($"/api/products?{query}", TestApi.Json))!;
    }

    private async Task<List<SuggestionBody>> SuggestAsync(string keyword) =>
        await SuggestAsync(_api.Anonymous(), keyword);

    private static async Task<List<SuggestionBody>> SuggestAsync(HttpClient client, string keyword) =>
        (await client.GetFromJsonAsync<List<SuggestionBody>>(
            $"/api/products/suggestions?q={Uri.EscapeDataString(keyword)}", TestApi.Json))!;

    private sealed record SuggestionBody(string Kind, int Id, string Slug, string Name, string? ImageUrl);
    private sealed record SlugBody(int Id, string Slug);
    private sealed record CategorySlugBody(int Id, string Slug);

    private sealed record SearchPageBody(List<TestApi.IdBody> Items, int TotalCount, RecoveryBody? Search);
    private sealed record RecoveryBody(string Term, string? SearchedInstead, CategorySuggestionBody? Category);
    private sealed record CategorySuggestionBody(int Id, string Slug, string Name);
}
