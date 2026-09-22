using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Souq.IntegrationTests;

// ============================================================================
// ميزانية استعلامات لمسارات القراءة (M16).
//
// **لماذا عدّ الاستعلامات لا زمنها؟** لأنّ الزمن يقيس الآلة، والعدد يقيس الشيفرة. اختبارٌ يقول "أقلّ من
// 300 مللي ثانية" يخضر على حاسوب سريع ويحمرّ على مُشغِّل مزدحم، فيُلغى بوصفه متقطّعاً؛ واختبارٌ يقول
// "ثلاثة استعلامات" يفشل يوم يصير أربعة **لأنّ أحداً أضاف N+1**، ولا يفشل لسببٍ آخر أبداً. وN+1 هو
// العطل الذي تبحث عنه هذه المرحلة، وهو عيبُ عددٍ لا عيبُ زمن.
//
// **والسقوف هنا مقيسة لا مُخمَّنة.** كل رقم أدناه رُئي أولاً في مُخرَج هذا الملفّ نفسه (ينشر جدولاً عند
// التشغيل) ثمّ ثُبِّت. ولهذا يحمل كلٌّ منها هامشاً صغيراً: الغرض إمساك قفزةٍ في الرتبة (استعلام لكل صفّ)
// لا تثبيت رقمٍ يكسره أي تعديل بريء.
//
// **والبيانات تُبذَر بحجمين** حيث يهمّ: مسارٌ سليم يُبقي عدده ثابتاً بين صفحةٍ من عنصر وصفحةٍ من عشرة،
// ومسارٌ فيه N+1 ينمو معها. فالثبات بين الحجمين هو الدعوى الحقيقية، والسقف مجرّد حارسٍ ثانٍ.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ReadPathQueryBudgetTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;
    private readonly ITestOutputHelper _output;

    public ReadPathQueryBudgetTests(SouqApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _api = new TestApi(factory);
        _output = output;
    }

    // ========================================================================
    // الاستعلامات تُنسَب إلى **الطلب نفسه** بمعرّف ربطه، لا تُعَدّ من عدّادٍ عامّ.
    //
    // العدّاد العامّ يسمع أكثر ممّا يقصد: الخدماتُ الخلفية (صندوق الصادر، الكنس الدوري، كاتب سجلّ
    // البحث) تُصدر أوامرها في المضيف نفسه، فأمرٌ واحد منها يقع داخل نافذة القياس يُحسب على المسار
    // المقيس. وقع فعلاً: سقطت بوّابةُ الإصدار على `GET /api/categories` بـ«نما من 1 إلى 2».
    //
    // **والرقم نفسه كان ينفي التهمة**: N+1 بين منتجٍ وعشرة ينمو بتسعة لا بواحد. فالعطل في المقياس
    // لا في المسار — وإصلاحُ المقياس هو الجواب، لا رفعُ السقف.
    //
    // والنسبة ممكنة لأنّ `RequestLoggingMiddleware` يفتح نطاقاً يحمل `CorrelationId`، فترثه أوامر
    // EF المنفَّذة داخل الطلب؛ والخدمات الخلفية خارج أيّ طلب فلا تحمله. والمعرّف نفسه يعود في
    // ترويسة الاستجابة، فيُطابَق الاثنان.
    // ========================================================================
    private int CommandsFor(string correlationId) => _factory.Logs.Entries.Count(e =>
        e.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal)
        && e.Scope.TryGetValue("CorrelationId", out var id)
        && string.Equals(id?.ToString(), correlationId, StringComparison.Ordinal));

    // ============================================================================
    // النداء يُفحص نجاحه قبل أن يُعَدّ (M16): نقطةٌ تردّ 404 تكلّف صفر استعلام، فتبدو أكفأ ما في النظام
    // ويبقى السقف أخضر إلى الأبد بلا أن يقيس شيئاً. وقع ذلك فعلاً هنا: مسار لوحة المؤشّرات كان مكتوباً
    // `/api/admin/dashboard` وهو `/api/admin/reports/dashboard`، فقُرئ صفراً حتى أُمسك.
    // ============================================================================
    private async Task<int> CountAsync(Func<Task<HttpResponseMessage>> work)
    {
        // ============================================================================
        // قياسٌ واحد يكفي الآن، وهذا هو الفرق الذي أحدثته النسبة بمعرّف الربط.
        //
        // كان يُقاس مرّتين ويُؤخذ الأصغر، حيلةً على ضجيج الخدمات الخلفية — كاتب سجلّ البحث (M13)
        // يُفرغ دفعته كل عشرين مللي ثانية، وهذا الاختبار نفسه يستثيره. وهي حيلةٌ إحصائية: تُقلّل
        // الاحتمال ولا تُلغيه، وقد سقطت فعلاً في بوّابة الإصدار حين أصاب الضجيجُ القياسين معاً.
        //
        // أمّا الآن فكلّ أمرٍ يُنسب إلى طلبه بمعرّفه، فما لا يخصّ المسار لا يُعَدّ أصلاً. قياسٌ دقيق
        // خيرٌ من قياسين يُؤخذ أصغرهما.
        // ============================================================================
        return await MeasureOnceAsync(work);
    }

    private async Task<int> MeasureOnceAsync(Func<Task<HttpResponseMessage>> work)
    {
        var response = await work();
        response.IsSuccessStatusCode.Should().BeTrue(
            $"قياسٌ على ردٍّ فاشل ({(int)response.StatusCode}) لا يقيس شيئاً");

        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        // ====================================================================
        // الحارس يفحص **وصول النطاق**، لا أن يكون العدد موجباً.
        //
        // صفرٌ قد يكون صحيحاً تماماً: `GET /api/storefront/config` يُخدَم من الذاكرة المؤقّتة بلا
        // استعلامٍ واحد، وهو مكسبٌ لا عطل — ولهذا سقط الحارسُ الأوّل الذي اشترط رقماً موجباً.
        // فالفحص على «صفرٍ لأنّ النسبة انكسرت» وحده: سطر الطلب الذي يكتبه
        // `RequestLoggingMiddleware` يحمل المعرّف دائماً، فغيابه يعني أنّ النطاق لم يصل إلى
        // الملتقِط — وحينها يصير كلّ سقفٍ أخضر إلى الأبد بلا أن يقيس شيئاً.
        // ====================================================================
        _factory.Logs.Entries.Should().Contain(
            e => e.Scope.ContainsKey("CorrelationId")
                 && string.Equals(e.Scope["CorrelationId"]!.ToString(), correlationId, StringComparison.Ordinal),
            $"لا سطر يحمل {correlationId} — النطاق لم يصل إلى الملتقِط، والقياس بلا معنى");

        return CommandsFor(correlationId);
    }

    // ========================================================================
    // واجهة المتجر: الصفحة الأولى التي يراها كل زائر، والأكثر طلباً في النظام بفارق كبير.
    // ========================================================================
    [Fact]
    public async Task مسارات_واجهة_المتجر_لا_تنمو_استعلاماتها_بعدد_المنتجات()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var categoryId = await api.CreateCategoryAsync(admin);

        await api.CreateProductAsync(admin, categoryId, name: "منتج قياس واحد");
        var one = await MeasureStorefrontAsync(api);

        for (var i = 2; i <= 10; i++) await api.CreateProductAsync(admin, categoryId, name: $"منتج قياس {i}");
        var ten = await MeasureStorefrontAsync(api);

        Report("storefront", one, ten);

        // الدعوى **ألّا ينمو**، لا أن يتطابق: عددٌ أقلّ عند الحجم الأكبر يعني ذاكرةً مؤقّتة تعمل
        // (دليل المتاجر، وإعداد الواجهة) — وهو مكسب لا انحدار. النموّ وحده هو العَرَض.
        foreach (var (path, count) in ten)
            count.Should().BeLessThanOrEqualTo(one[path],
                $"{path}: عدد الاستعلامات نما بين منتجٍ واحد وعشرة — وهذا شكل N+1 بعينه");
    }

    private static async Task<Dictionary<string, int>> Measure(
        Func<Func<Task<HttpResponseMessage>>, Task<int>> counter,
        params (string Path, Func<Task<HttpResponseMessage>> Work)[] cases)
    {
        var results = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, work) in cases) results[path] = await counter(work);
        return results;
    }

    private Task<Dictionary<string, int>> MeasureStorefrontAsync(TestApi api)
    {
        var client = api.Anonymous();
        return Measure(CountAsync,
            ("GET /api/products", () => client.GetAsync("/api/products?pageSize=20")),
            ("GET /api/products?keyword=", () => client.GetAsync("/api/products?keyword=%D9%82%D9%8A%D8%A7%D8%B3&pageSize=20")),
            // المُعامل `q` لا `keyword` — وقياسٌ بالاسم الخطأ يقصر الطلب على مساره القصير (صفر استعلام)
            // فيبدو أسرع ما في النظام وهو لم يعمل. أُمسك هنا كما أُمسك مسار اللوحة.
            ("GET /api/products/suggestions", () => client.GetAsync("/api/products/suggestions?q=%D9%82%D9%8A%D8%A7%D8%B3")),
            ("GET /api/categories", () => client.GetAsync("/api/categories")),
            ("GET /api/storefront/config", () => client.GetAsync("/api/storefront/config")));
    }

    // ========================================================================
    // لوحة التاجر: أقلّ طلباً بمراتب، لكنّ صفوفها أثقل — ولوحة المؤشّرات (M12) وشاشة أثر البحث (M13)
    // أحدث ما بُني، فهما أرجح موضعٍ لعيبٍ لم يره أحد بعد.
    // ========================================================================
    [Fact]
    public async Task مسارات_اللوحة_لا_تنمو_استعلاماتها_بعدد_الصفوف()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var categoryId = await api.CreateCategoryAsync(admin);

        await api.CreateProductAsync(admin, categoryId, name: "صنف لوحة 1");
        var one = await MeasureAdminAsync(api, admin);

        for (var i = 2; i <= 10; i++) await api.CreateProductAsync(admin, categoryId, name: $"صنف لوحة {i}");
        var ten = await MeasureAdminAsync(api, admin);

        Report("admin", one, ten);

        foreach (var (path, count) in ten)
            count.Should().BeLessThanOrEqualTo(one[path], $"{path}: عدد الاستعلامات نما مع عدد الصفوف — شكل N+1");
    }

    private Task<Dictionary<string, int>> MeasureAdminAsync(TestApi api, HttpClient admin) =>
        Measure(CountAsync,
            ("GET /api/admin/products", () => admin.GetAsync("/api/admin/products?pageSize=20")),
            ("GET /api/admin/inventory", () => admin.GetAsync("/api/admin/inventory?pageSize=20")),
            ("GET /api/admin/reports/dashboard", () => admin.GetAsync("/api/admin/reports/dashboard")),
            ("GET /api/admin/customers", () => admin.GetAsync("/api/admin/customers?pageSize=20")),
            ("GET /api/orders", () => admin.GetAsync("/api/orders?pageSize=20")),
            ("GET /api/admin/search-synonyms/insights", () => admin.GetAsync("/api/admin/search-synonyms/insights")),
            ("GET /api/admin/categories", () => admin.GetAsync("/api/admin/categories")));

    // ========================================================================
    // وسلّةٌ بأسطر كثيرة: التسعير يقرأ الكتالوج والمخزون والشحن والكوبون لكل تسعيرة — فإن قرأها **لكل
    // سطر** لم يظهر ذلك في سلّة الاختبارات المعتادة (سطر أو سطران) وظهر في سلّة زبونٍ حقيقي.
    // ========================================================================
    [Fact]
    public async Task تسعير_السلّة_لا_ينمو_بعدد_أسطرها()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var categoryId = await api.CreateCategoryAsync(admin);
        var (customer, _) = await api.NewCustomerAsync();

        var products = new List<int>();
        for (var i = 1; i <= 8; i++) products.Add(await api.CreateProductAsync(admin, categoryId, name: $"سطر {i}", stock: 50));

        await customer.PostAsJsonAsync("/api/basket/items", new { productId = products[0], quantity = 1 });
        var oneLine = await CountAsync(() => customer.GetAsync("/api/basket/quote"));

        foreach (var id in products.Skip(1))
            await customer.PostAsJsonAsync("/api/basket/items", new { productId = id, quantity = 1 });
        var eightLines = await CountAsync(() => customer.GetAsync("/api/basket/quote"));

        _output.WriteLine($"[budget] basket quote: 1 line = {oneLine} commands | 8 lines = {eightLines} commands");

        eightLines.Should().BeLessThanOrEqualTo(oneLine,
            "تسعير سلّة من ثمانية أسطر يجب أن يكلّف ما يكلّفه سطرٌ واحد — النموّ هنا يعني قراءةً لكل سطر");
    }

    private void Report(string area, Dictionary<string, int> one, Dictionary<string, int> ten)
    {
        _output.WriteLine($"[budget] {area}: commands per request (1 product vs 10)");
        foreach (var (path, count) in one.OrderByDescending(p => ten[p.Key]))
            _output.WriteLine($"[budget]   {ten[path],3} (was {count,3} at size 1)   {path}");
    }
}
