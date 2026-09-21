using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
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

    // ============================================================================
    // **إيقاف «إدارة فقط»** — قرار المالك C-17 = B (2026-09-21)، وهو تغيير سلوك مقصود.
    //
    // ما كان: الصلاحية لا تفتح متجراً موقوفاً، فكانت لوحة التاجر 503 كاملةً. وR-08 كان قد فتح
    // الدخول وإعدادَ الواجهة وحدهما — أي أنّ التاجر يدخل ليرى أنه لا يستطيع فعل شيء.
    //
    // ما صار: **الإدارة تعمل** (التاجر يُصلح سبب الإيقاف)، والواجهة مغلقة على المتسوّق، والشراء
    // مرفوضٌ عند الخادم لا في المتصفّح — ومسارات الشراء ليست محروسة بصلاحية، فتسقط في فرع الإغلاق
    // بالبناء لا بالسهو، وهذا الاختبار هو ما يثبّت ذلك.
    // ============================================================================
    [Fact]
    public async Task متجر_موقوف_إدارته_تعمل_وواجهته_مغلقة_والشراء_مرفوض_عند_الخادم()
    {
        // المتجر يُنشأ فعّالاً ويُوقَف بعد أن يصير له عميل: التسجيل مغلق على الموقوف، فالعميل الذي
        // يجب أن يُمنع من الشراء لا يمكن أن يوجد أصلاً إن بدأ المتجر موقوفاً — وهذا هو الترتيب
        // الواقعي: متجرٌ كان يبيع ثم أُوقف.
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var (customer, _) = await api.NewCustomerAsync();

        var owner = await _api.PlatformOwnerAsync();
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/status", new { action = "Suspend" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        var catalog = await api.Anonymous().GetAsync("/api/products");
        catalog.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemCodeAsync(catalog)).Should().Be("StoreUnavailable");

        // إعداد الواجهة يجيب: به وحده تعرض الواجهة صفحة "المتجر غير متاح" بهويّة المتجر.
        (await api.Anonymous().GetAsync("/api/storefront/config")).StatusCode.Should().Be(HttpStatusCode.OK);

        // لوحة التاجر تعمل — وهذا هو معنى القرار.
        var admin = await api.AdminAsync();
        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK,
            "C-17 = B: نقاط الإدارة محروسة بصلاحية، والصلاحية تفتح متجراً موقوفاً");
        (await admin.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.OK,
            "وقائمة طلبات المتجر إدارةٌ أيضاً (orders.view): التاجر يشحن ما بِيع قبل الإيقاف");

        // وما هو للمتسوّق يبقى مغلقاً: قائمة «طلباتي» بلا صلاحية، فتسقط في فرع الإغلاق. وما يبقى
        // للمشتري هو رابط التتبّع وحده — وهو ما يليه من اختبار.
        (await customer.GetAsync("/api/orders/mine")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "«طلباتي» صفحة متجر لا إدارة");

        // والشراء مرفوض عند الخادم: سلّةٌ وطلبٌ، وكلاهما بلا صلاحية فكلاهما مغلق.
        (await customer.GetAsync("/api/basket")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "السلّة مسار شراء، والمتجر الموقوف لا يبيع");
        (await customer.PostAsJsonAsync("/api/orders", new { }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                "وإنشاء الطلب يُرفض عند الخادم لا في المتصفّح وحده");

        (await api.Anonymous().PostAsJsonAsync("/api/auth/register",
                new { fullName = "زائر", email = $"x-{Guid.NewGuid():N}@souq.test", password = "Customer-Pass-1" }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ============================================================================
    // TD-61 — المتجر **المؤرشف**، وهو علاقة تجارية انتهت.
    //
    // كان سلوكه مطابقاً للموقوف حرفياً: `IsOpen` تُسقط الحالتين على الفرع نفسه. وقد حذّر هذا
    // الاختبار عند كتابته من أنّ «يوم يُجاب قرار المالك C-17 يُعدَّل هذا الاختبار عمداً» — وهذا
    // هو اليوم: C-17 = B فرّق بينهما، فصارت **الصلاحية تفتح الموقوف ولا تفتح المؤرشف**.
    //
    // فما يثبّته الآن هو الفرق نفسه: الأرشفة نهائية، لا لوحة ولا تتبّع ولا تسجيل — وما يبقى هو
    // إعداد الواجهة والدخول وحدهما، كي تُعرض شاشةٌ بهويّة المتجر لا صفحة خطأ عارية (R-08).
    // ============================================================================
    [Fact]
    public async Task متجر_مؤرشف_لا_تفتحه_صلاحية_بخلاف_الموقوف()
    {
        var store = await _factory.CreateStoreAsync(TenantStatus.Archived);
        var api = _api.ForStore(store);

        var catalog = await api.Anonymous().GetAsync("/api/products");
        catalog.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemCodeAsync(catalog)).Should().Be("StoreUnavailable");

        // إعداد الواجهة يجيب: تعرض الواجهة "غير متاح" بهويّة المتجر لا صفحة خطأ عارية (R-08).
        (await api.Anonymous().GetAsync("/api/storefront/config")).StatusCode.Should().Be(HttpStatusCode.OK);

        // والصلاحية **لا** تفتح متجراً مؤرشفاً — بخلاف الموقوف، وهذا هو الفرق الذي أحدثه C-17 = B.
        var admin = await api.AdminAsync();
        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "المؤرشف نهائي: لا لوحة. والموقوف يفتحها — فإن تساوى الردّان فقد عاد الفرع الواحد");
        (await admin.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // ولا يُسجَّل فيه زبون جديد: متجرٌ انتهى عقده لا يكتسب عملاء.
        (await api.Anonymous().PostAsJsonAsync("/api/auth/register",
                new { fullName = "زائر", email = $"a-{Guid.NewGuid():N}@souq.test", password = "Customer-Pass-1" }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ============================================================================
    // TD-66 — **مُصلَح**: الأرشفة تُبطل جلسات المتجر كلها، والإيقاف لا يُبطلها.
    //
    // كان هذا الاختبار يثبّت العطب كما هو، بسطرٍ يقول: «إن احمرّ هذا السطر فقد أُصلح TD-66 —
    // حدّث الاختبار عمداً». وقد أُصلح، فهذا هو التحديث المقصود.
    //
    // والفرق بين الحالتين هو قرار المالك C-17 = B نفسه: الإيقاف مؤقّت ومعناه «إدارة فقط»، فالتاجر
    // يبقى داخلاً ليُصلح سببه؛ والأرشفة نهائية، وهي التي كانت تُبقي جلسةً قابلة للتجديد بلا نهاية
    // لأن `/api/auth/refresh` مفتوحةٌ للمغلق عمداً.
    // ============================================================================
    [Fact]
    public async Task الأرشفة_تُبطل_جلسات_المتجر_والإيقاف_يُبقيها_TD66()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var owner = await _api.PlatformOwnerAsync();

        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK);

        // الإيقاف أوّلاً: الجلسة تبقى، واللوحة تعمل — وهذا نصف القرار.
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/status", new { action = "Suspend" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.OK,
            "الإيقاف لا يُبطل جلسة التاجر: هو مَن يُصلح سببه");

        // ثم الأرشفة: الجلسة تسقط، وليس لأن بوّابة الحالة أغلقت النقطة — بل لأن الختم دُوِّر.
        // والدليل هو `/api/auth/me` نفسها: مفتوحةٌ للمتجر المغلق عمداً، فلو بقيت الجلسة لأجابت 200.
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/status", new { action = "Archive" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "TD-66 مُصلَح: الأرشفة تُدوّر ختم الأمان، فتوكن الوصول لم يعد يطابق");

        // ورموز التجديد أُبطلت في الحفظ نفسه: جلسةٌ مؤرشفة لا تُجدَّد.
        var live = await _api.WithDbAsync(db => db.RefreshTokens.IgnoreQueryFilters()
            .CountAsync(t => t.TenantId == store.Tenant.Id && t.RevokedAt == null));
        live.Should().Be(0, "رمز تجديد حيّ في متجر مؤرشف يعني جلسةً بلا نهاية");
    }

    // ============================================================================
    // **رابط تتبّع طلبٍ مدفوع يبقى عاملاً والمتجر موقوف** — نصف قرار C-17 = B الذي يخصّ المشتري.
    //
    // المشتري دفع قبل الإيقاف، والإيقاف مسألة بين المنصّة والتاجر لا ذنب له فيها. وقبل هذا كانت
    // النقطة بلا استثناء إغلاق، فكان الإيقاف **يُعمي المشتري عن طلبٍ دفع ثمنه** — وهي الملاحظة
    // التي رفعها الملخّص للمالك، وقد تحقّقت من الكود لا من الوثائق.
    //
    // وللمؤرشف لا تُفتح: الأرشفة نهائية، وهو الفرق نفسه مرّةً أخرى.
    // ============================================================================
    [Fact]
    public async Task تتبّع_طلب_مدفوع_يعمل_والمتجر_موقوف_ولا_يعمل_والمتجر_مؤرشف()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 12m, stock: 3);
        var (customer, _) = await api.NewCustomerAsync();

        var created = await api.PlaceOrderAsync(customer, productId, 1);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = await _api.WithDbAsync(db => db.Orders.IgnoreQueryFilters()
            .Where(o => o.TenantId == store.Tenant.Id).Select(o => o.Id).SingleAsync());
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await _api.WithDbAsync(db => db.Orders.IgnoreQueryFilters()
            .Where(o => o.Id == orderId).Select(o => o.TrackingToken).SingleAsync());

        var owner = await _api.PlatformOwnerAsync();
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/status", new { action = "Suspend" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        var tracked = await api.Anonymous().GetAsync($"/api/orders/track/{token}");
        tracked.StatusCode.Should().Be(HttpStatusCode.OK,
            "C-17 = B: المشتري يتتبّع طلباً دفع ثمنه، وإن أُوقف المتجر بعده");

        (await owner.PostAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/status", new { action = "Archive" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        var afterArchive = await api.Anonymous().GetAsync($"/api/orders/track/{token}");
        afterArchive.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "والأرشفة نهائية: لا تتبّع بعدها");
        (await ProblemCodeAsync(afterArchive)).Should().Be("StoreUnavailable");
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
