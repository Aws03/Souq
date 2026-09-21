using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.Domain.Platform;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// تحديد المستأجر من المضيف عبر الخادم الحقيقي (ADR-0006): مضيف مجهول، متجر موقوف أو قيد التجهيز،
// مضيف المنصّة، ووسائل التطوير المسموحة في Testing ({slug}.localhost، X-Tenant).
[Collection(IntegrationCollection.Name)]
public class TenantResolutionTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public TenantResolutionTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task مضيف_بلا_متجر_يعيد_404_StoreNotFound_بلا_متجر_احتياطي()
    {
        var response = await _api.Client("unknown-store.example").GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(response)).Should().Be("StoreNotFound");
    }

    // R-08: المتجر يُغلق على زبائنه، لا على صاحبه. قبل الإصلاح كان 503 يشمل إعداد الواجهة والدخول معاً، فيرى صاحب المتجر
    // صفحة خطأ عارية ولا يستطيع الدخول ليعرف لماذا أُوقف متجره.
    [Fact]
    public async Task متجر_موقوف_يُغلق_على_الزوّار_ويبقي_إعداد_الواجهة_ودخول_إدارته()
    {
        var store = await _factory.CreateStoreAsync(TenantStatus.Suspended);
        var api = _api.ForStore(store);

        var catalog = await api.Anonymous().GetAsync("/api/products");
        catalog.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemCodeAsync(catalog)).Should().Be("StoreUnavailable");

        // إعداد الواجهة يجيب: به وحده تعرض الواجهة صفحة "المتجر غير متاح" بهويّة المتجر.
        (await api.Anonymous().GetAsync("/api/storefront/config")).StatusCode.Should().Be(HttpStatusCode.OK);

        // الدخول متاح لإدارته، وما وراءه يبقى مغلقاً: الصلاحية لا تفتح متجراً موقوفاً.
        var admin = await api.AdminAsync();
        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await admin.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // ولا يفتح التسجيل ولا الشراء: الإتاحة نقطةً نقطةً، لا للمتحكّم كله.
        (await api.Anonymous().PostAsJsonAsync("/api/auth/register",
                new { fullName = "زائر", email = $"x-{Guid.NewGuid():N}@souq.test", password = "Customer-Pass-1" }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ============================================================================
    // TD-61 — المتجر **المؤرشف**، وهو علاقة تجارية انتهت.
    //
    // سلوكه اليوم مطابق للموقوف حرفياً: `TenantAvailabilityMiddleware.IsOpen` تُسقط الحالتين على
    // الفرع نفسه (`_ => Has<AvailableWhenStoreClosed>`). ولم يكن شيء يثبّت ذلك — `TenantStatus.Archived`
    // لم تظهر في مجموعة التكامل إلّا في اختبار نطاق المنسّقات وفي المصنع. فتعديلُ حالةٍ واحدة في
    // ذلك `switch` كان يُعيد فتح واجهة متجر انتهى عقده **ولا يحمرّ شيء**.
    //
    // وما يثبّته هذا الاختبار هو **السلوك القائم**، لا سلوكاً مرغوباً: يوم يُجاب قرار المالك C-17
    // (ماذا يفعل متجر مغلق، وهل للمؤرشف مصير مختلف) يُعدَّل هذا الاختبار عمداً — وهو بالضبط الفرق
    // بين تغييرٍ مقصود وانزلاقٍ صامت.
    // ============================================================================
    [Fact]
    public async Task متجر_مؤرشف_مغلق_كالموقوف_ولا_يُفتح_بصلاحية()
    {
        var store = await _factory.CreateStoreAsync(TenantStatus.Archived);
        var api = _api.ForStore(store);

        var catalog = await api.Anonymous().GetAsync("/api/products");
        catalog.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemCodeAsync(catalog)).Should().Be("StoreUnavailable");

        // إعداد الواجهة يجيب: تعرض الواجهة "غير متاح" بهويّة المتجر لا صفحة خطأ عارية (R-08).
        (await api.Anonymous().GetAsync("/api/storefront/config")).StatusCode.Should().Be(HttpStatusCode.OK);

        // والصلاحية لا تفتح متجراً مؤرشفاً، كما لا تفتح موقوفاً.
        var admin = await api.AdminAsync();
        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await admin.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // ولا يُسجَّل فيه زبون جديد: متجرٌ انتهى عقده لا يكتسب عملاء.
        (await api.Anonymous().PostAsJsonAsync("/api/auth/register",
                new { fullName = "زائر", email = $"a-{Guid.NewGuid():N}@souq.test", password = "Customer-Pass-1" }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ============================================================================
    // TD-66، مثبَّتاً كما هو لا كما ينبغي أن يكون: أرشفةُ متجر **لا تُبطل جلسات إدارته**.
    // `ChangeTenantStatusHandler` يغيّر الحالة ويُبطل دليل المتاجر ويقف — بينما تعطيلُ **حساب**
    // يُدوّر ختم الأمان ويُبطل رموز التجديد.
    //
    // وغير قابل للاستغلال اليوم: كل نقطة مُصرَّحة تجيب 503 لمتجر مغلق. لكن `/api/auth/refresh`
    // مُعلَّمة `AvailableWhenStoreClosed` عمداً، فمديرو متجرٍ مؤرشف يحتفظون بجلسات قابلة للتجديد
    // بلا نهاية — ويصير ذلك ثغرةً حيّة يوم تعمل أيّ نقطة `AvailableWhenStoreClosed` عملاً حقيقياً،
    // أو يوم تُؤتمت الأرشفة بالفوترة بدل أن يكتبها إنسان.
    //
    // الاختبار يوثّق الحقيقة القائمة ويجعل إصلاحها **مرئياً**: من يُصلح TD-66 يرى هذا الاختبار
    // يحمرّ فيعرف أنه غيّر ما قصد تغييره.
    // ============================================================================
    [Fact]
    public async Task أرشفة_متجر_لا_تُبطل_جلسة_إدارته_اليوم_TD66()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();

        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK);

        var owner = await _api.PlatformOwnerAsync();
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/status", new { action = "Archive" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        // الجلسة ما زالت تُقبل — النقطة تُغلق بحالة المتجر، لا لأن الجلسة أُبطلت.
        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK,
            "TD-66: الأرشفة لا تُبطل الجلسة اليوم. إن احمرّ هذا السطر فقد أُصلح TD-66 — حدّث الاختبار عمداً");
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "وما يحمي المتجر المغلق هو بوّابة الحالة لا إبطال الجلسة");
    }

    [Fact]
    public async Task متجر_قيد_التجهيز_مغلق_للزوّار_ومفتوح_لإدارته()
    {
        var store = await _factory.CreateStoreAsync(TenantStatus.Provisioning);
        var api = _api.ForStore(store);

        (await api.Anonymous().GetAsync("/api/products")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var admin = await api.AdminAsync();
        (await admin.GetAsync("/api/coupons")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task مضيف_المنصّة_لا_يخدم_نقاط_المتاجر()
    {
        var response = await _api.Client("admin.localhost").GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task في_الاختبار_يُحدَّد_المتجر_بنطاق_localhost_الفرعي_أو_بترويسة_التطوير()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var productId = await storeApi.CreateProductAsync(await storeApi.AdminAsync());

        (await _api.Client($"{store.Tenant.Slug}.localhost").GetAsync($"/api/products/{productId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var viaHeader = _api.Anonymous();
        viaHeader.DefaultRequestHeaders.Add("X-Tenant", store.Tenant.Slug);
        (await viaHeader.GetAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // localhost بلا ترويسة = المتجر الافتراضي، ولا يرى منتج المتجر الآخر.
        (await _api.Anonymous().GetAsync($"/api/products/{productId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task سطر_سجلّ_الطلب_يحمل_المتجر()
    {
        var store = await _factory.CreateStoreAsync();

        await _api.ForStore(store).Anonymous().GetAsync("/api/categories");

        _factory.Logs.Entries.Should().Contain(e =>
            e.Category.EndsWith("RequestLoggingMiddleware")
            && e.Scope.ContainsKey("TenantId") && Equals(e.Scope["TenantId"], store.Tenant.Id));
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code;
}
