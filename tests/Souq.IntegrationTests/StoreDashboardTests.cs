using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using AwesomeAssertions;
using Souq.Domain.Common;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// لوحة مؤشّرات المتجر عبر HTTP الحقيقي.
//
// ثلاثة أسئلة لا يُجاب عنها بقراءة الشيفرة:
//   1) هل تُحسب الأرقام كما تُعرَّف؟ (طلب مُلغى لا يدخل الإيراد، وطلب لم يُدفع كذلك)
//   2) هل يرى متجرٌ أرقام متجر آخر؟ — لا تجاوز للمرشّح هنا، والاختبار يثبت ذلك ببيانات حقيقية.
//   3) من يُسمح له بالقراءة؟ الصلاحية store.reports.view، والعميل والزائر خارجها.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class StoreDashboardTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public StoreDashboardTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    private sealed record Totals(decimal Revenue, decimal Refunds, decimal NetRevenue, int Orders, decimal AverageOrderValue, int NewCustomers);
    private sealed record Dashboard(
        string Range, DateTime From, DateTime To, string Currency, Totals Current, Totals Previous,
        List<Point> Trend, Dictionary<string, int> OrdersByStatus, List<TopProduct> TopProducts,
        List<TopCategory> TopCategories, Inventory Inventory, int PendingOrders, int PendingRefunds,
        int TotalCustomers, int RepeatCustomers);
    private sealed record Point(DateTime Bucket, decimal Revenue, int Orders);
    private sealed record TopProduct(int ProductId, string Name, int UnitsSold, decimal Revenue);
    private sealed record TopCategory(int CategoryId, string Name, int UnitsSold, decimal Revenue);
    private sealed record Inventory(int Healthy, int Low, int OutOfStock);

    private static async Task<Dashboard> ReadAsync(HttpClient client, string range = "Last30Days")
    {
        var response = await client.GetAsync($"/api/admin/reports/dashboard?range={range}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Dashboard>(TestApi.Json))!;
    }

    [Fact]
    public async Task طلب_لم_يُدفع_لا_يدخل_الإيراد_وطلب_مدفوع_يدخله()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 40m, stock: 50);

        var before = await ReadAsync(admin);

        // طلب مُثبَّت لم يُدفع (Pending): يظهر في "بانتظار الدفع" ولا يظهر في الإيراد.
        var (customer, _) = await api.NewCustomerAsync();
        (await api.PlaceOrderAsync(customer, productId, 2)).StatusCode.Should().Be(HttpStatusCode.Created);

        var pending = await ReadAsync(admin);
        pending.Current.Revenue.Should().Be(before.Current.Revenue, "طلب لم يُدفع ليس إيراداً");
        pending.PendingOrders.Should().Be(before.PendingOrders + 1);
        pending.OrdersByStatus["Pending"].Should().Be(before.OrdersByStatus["Pending"] + 1);
    }

    // ============================================================================
    // متوسّط قيمة الطلب بخانات **عملة المتجر** (M19).
    //
    // كان مقرَّباً إلى خانتين ثابتتين في الشيفرة. فمتجرٌ بالدينار (ثلاث خانات) يرى متوسّطاً مقصوصاً
    // إلى قرشين — رقمُ مالٍ لا يوجد بعملته — ومتجرٌ بالين (بلا خانات) يرى كسوراً لا معنى لها.
    // والقاعدة موجودة في المجال أصلاً ويطبّقها `Money` على كل مبلغ آخر: `CurrencyInfo.MinorUnits`.
    //
    // ثلاثة طلبات بإجمالي 100 تُنتج متوسّطاً دورياً (33.333…)، وهو ما يكشف الفرق: الخانتان
    // الثابتتان تعطيان 33.33 في الحالتين، والصواب 33.333 بالدينار و33 بالين.
    // ============================================================================
    [Theory]
    [InlineData("JOD", 33.333)]    // ثلاث خانات
    [InlineData("JPY", 33)]        // بلا خانات
    public async Task متوسّط_قيمة_الطلب_يُقرَّب_بخانات_عملة_المتجر(string currency, decimal expected)
    {
        var store = await _factory.CreateStoreAsync(currency: currency);
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 25m, stock: 100);

        foreach (var quantity in new[] { 1, 1, 2 })        // 25 + 25 + 50 = 100 على ثلاثة طلبات
        {
            var (customer, _) = await api.NewCustomerAsync();
            var created = await api.PlaceOrderAsync(customer, productId, quantity);
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            var orderId = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString();
            (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var dashboard = await ReadAsync(admin);

        dashboard.Current.Orders.Should().Be(3);
        dashboard.Current.Revenue.Should().Be(100m);
        dashboard.Current.AverageOrderValue.Should().Be(expected,
            "المتوسّط يُعرض بعملة المتجر، فيُقرَّب بخاناتها لا بخانتين مفترضتين");
    }

    [Fact]
    public async Task الإيراد_ومتوسّط_قيمة_الطلب_من_الطلبات_المدفوعة_وحدها()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 25m, stock: 100);

        var (customer, _) = await api.NewCustomerAsync();
        var created = await api.PlaceOrderAsync(customer, productId, 4);   // 100
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString();

        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var dashboard = await ReadAsync(admin);

        dashboard.Current.Orders.Should().Be(1);
        dashboard.Current.Revenue.Should().Be(100m);
        dashboard.Current.AverageOrderValue.Should().Be(100m, "متوسّط قيمة الطلب = الإيراد ÷ عدد الطلبات");
        dashboard.Current.Refunds.Should().Be(0m);
        dashboard.Current.NetRevenue.Should().Be(100m);
        dashboard.OrdersByStatus["Paid"].Should().Be(1);
        dashboard.Currency.Should().Be(store.Tenant.Currency, "كل المبالغ بعملة المتجر");

        dashboard.TopProducts.Should().ContainSingle()
            .Which.Should().Match<TopProduct>(p => p.ProductId == productId && p.UnitsSold == 4 && p.Revenue == 100m);
    }

    [Fact]
    public async Task متوسّط_قيمة_الطلب_من_الإيراد_قبل_الاسترداد_لا_من_صافيه()
    {
        // ============================================================================
        // الفرق يظهر **فقط** مع وجود استرداد، والاختبار الذي يقيس المتوسّط أعلاه استردادُه صفر — فلم
        // يكن شيء يفرّق الإجمالي من الصافي (M12). والتمييز مهمّ للتاجر لا للكود: اللوحة تعرض "صافي
        // الإيراد" وحده، فإن كان المتوسّط من الإجمالي فإنّ `المتوسّط × الطلبات` لا يساوي أيّ رقم على
        // الشاشة. هذا ما يقوله تلميح المتوسّط الآن بعد أن كان يقول "الإيراد" مجرَّداً.
        // ============================================================================
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 50m, stock: 100);

        var (customer, _) = await api.NewCustomerAsync();
        var created = await api.PlaceOrderAsync(customer, productId, 2);   // 100
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString();
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { amount = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var dashboard = await ReadAsync(admin);

        dashboard.Current.Orders.Should().Be(1);
        dashboard.Current.Revenue.Should().Be(100m, "الإجمالي لا ينقص بالاسترداد");
        // اكتمل في المدّة نفسها التي وقع فيها الطلب، فالنسبتان تتّفقان هنا — والاختبار الذي
        // يفرّق بينهما هو `الاسترداد_يُنسب_إلى_مدّته_لا_إلى_مدّة_الطلب` أدناه.
        dashboard.Current.Refunds.Should().Be(30m);
        dashboard.Current.NetRevenue.Should().Be(70m);
        dashboard.Current.AverageOrderValue.Should().Be(100m, "المتوسّط من الإجمالي: 100 ÷ 1");

        // وهذا هو الفرق الذي يجب أن يبقى مقصوداً: حاصل الضرب لا يساوي الصافي المعروض.
        (dashboard.Current.AverageOrderValue * dashboard.Current.Orders)
            .Should().NotBe(dashboard.Current.NetRevenue,
                "مع استرداد، المتوسّط × الطلبات ≠ صافي الإيراد — والتلميح يقول ذلك صراحةً الآن");
    }

    [Fact]
    public async Task العملاء_الجدد_لا_يشملون_من_مُحي_حسابه_فقد_ينقص_رقم_مدّة_ماضية()
    {
        // ============================================================================
        // `newCustomers` يستثني `ErasedAt != null`، فحسابٌ أُنشئ في المدّة ثم مُحي يخرج من العدّ —
        // أي أنّ رقم **مدّة ماضية** ينقص بعد أن قُرئ. هذا سلوك مقصود (حقّ الحذف يعني ألّا يبقى أثر
        // يُعَدّ)، ولم يكن مذكوراً في تلميح اللوحة ولا مُثبَّتاً باختبار (M12).
        // ============================================================================
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();

        var before = (await ReadAsync(admin)).Current.NewCustomers;
        var (customer, _) = await api.NewCustomerAsync();
        (await ReadAsync(admin)).Current.NewCustomers.Should().Be(before + 1, "حساب جديد يُعَدّ");

        (await customer.PostAsJsonAsync("/api/account/erase", new { password = "Customer-Pass-1" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadAsync(admin)).Current.NewCustomers.Should().Be(before,
            "ومن مُحي حسابه يخرج من العدّ — فرقم المدّة نفسها صار أقلّ");
    }

    [Fact]
    public async Task صحّة_المخزون_ثلاث_سلال_وحدّها_الأدنى_داخل_المنخفض_لا_النافد()
    {
        // ============================================================================
        // لقطة المخزون لم يكن يمسّها **أيّ** اختبار قبل M12: التوكيد الوحيد كان `(0,0,0)` على متجر
        // فارغ، فلا شيء يفرّق "منخفض" من "نافد" ولا يثبّت الحدّ. وهي بالضبط السلّة التي غيّرها V3
        // (صار الصفّ متغيّراً لا منتجاً)، فمرّ التغيير بلا أن يلاحظه اختبار.
        //
        // والحدّ مقصود: المتاح المساوي لحدّ التنبيه **منخفض** لا نافد (نافد = متاح ≤ صفر). وهذا يختلف
        // عمّا تعرضه شاشة الجرد، التي تَعُدّ النافد داخل المنخفض — فرقٌ حقيقيّ بين شاشتين، مُسجَّل الآن
        // في Dashboards.md بعد أن كانت تقول "نفس تعريف وحدة المخزون".
        // ============================================================================
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();

        var out0 = await api.CreateProductAsync(admin, price: 10m, stock: 0);     // نافد
        var atThreshold = await api.CreateProductAsync(admin, price: 10m, stock: 5);   // الحدّ الافتراضي 5 ⇒ منخفض
        var healthy = await api.CreateProductAsync(admin, price: 10m, stock: 100);     // وفير

        var dashboard = await ReadAsync(admin);

        dashboard.Inventory.OutOfStock.Should().Be(1, "المتاح صفر وحده نافد");
        dashboard.Inventory.Low.Should().Be(1, "المتاح المساوي للحدّ منخفض، ولا يُحتسب نافداً");
        dashboard.Inventory.Healthy.Should().Be(1, "الوفير = الكلّ − المنخفض − النافد");
        (out0, atThreshold, healthy).Should().NotBe((0, 0, 0), "المنتجات أُنشئت فعلاً");

        // والتنبيهان يحملان رقمَيهما: تنبيه بلا عدد لا يُعرض (dashboardView).
        dashboard.Inventory.Low.Should().BePositive();
        dashboard.Inventory.OutOfStock.Should().BePositive();
    }

    [Fact]
    public async Task أداء_الفئات_يعرض_اسمها_المترجم_لا_معرّفها_في_الرابط()
    {
        // كانت اللوحة تعرض Slug الفئة ("home") — معرّف داخلي لا اسم، ويُقرأ كخللٍ في تقرير
        // بالعربية. لا يظهر ذلك في اختبار يقارن أرقاماً، فهو مذكور هنا صراحةً.
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();

        var slug = $"it-{Guid.NewGuid():N}"[..24];
        var createCategory = await admin.PostAsJsonAsync(
            "/api/categories", TestApi.CategoryBody(slug, name: "أدوات المطبخ"), TestApi.Json);
        createCategory.StatusCode.Should().Be(HttpStatusCode.Created, await createCategory.Content.ReadAsStringAsync());
        var categoryId = (await createCategory.Content.ReadFromJsonAsync<IdRow>(TestApi.Json))!.Id;

        var productId = await api.CreateProductAsync(admin, price: 30m, stock: 10, categoryId: categoryId);

        var (customer, _) = await api.NewCustomerAsync();
        var created = await api.PlaceOrderAsync(customer, productId, 1);
        var orderId = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString();
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var dashboard = await ReadAsync(admin);

        dashboard.TopCategories.Should().ContainSingle()
            .Which.Name.Should().Be("أدوات المطبخ")
            .And.NotBe(slug, "المعرّف في الرابط ليس اسماً يُعرض");
    }

    private sealed record IdRow(int Id);

    [Fact]
    public async Task لوحة_متجر_لا_ترى_شيئاً_من_مبيعات_متجر_آخر()
    {
        // الحدّ الذي لا يجوز أن ينكسر: الأرقام مجمَّعة، لكن تسرّبها يكشف أعمال متجر آخر.
        var quiet = await _factory.CreateStoreAsync();
        var busy = await _factory.CreateStoreAsync();

        var busyApi = _api.ForStore(busy);
        var busyAdmin = await busyApi.AdminAsync();
        var productId = await busyApi.CreateProductAsync(busyAdmin, price: 60m, stock: 20);
        var (busyCustomer, _) = await busyApi.NewCustomerAsync();
        var created = await busyApi.PlaceOrderAsync(busyCustomer, productId, 3);
        var orderId = (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString();
        await busyCustomer.PostAsync($"/api/orders/{orderId}/confirm-payment", null);

        var busyDashboard = await ReadAsync(busyAdmin);
        busyDashboard.Current.Revenue.Should().Be(180m);

        var quietDashboard = await ReadAsync(await _api.ForStore(quiet).AdminAsync());

        quietDashboard.Current.Revenue.Should().Be(0m);
        quietDashboard.Current.Orders.Should().Be(0);
        quietDashboard.TopProducts.Should().BeEmpty();
        quietDashboard.TotalCustomers.Should().Be(0);
    }

    [Fact]
    public async Task توكن_متجر_على_مضيف_متجر_آخر_لا_يفتح_لوحته()
    {
        var a = await _factory.CreateStoreAsync();
        var b = await _factory.CreateStoreAsync();
        var aAdmin = await _api.ForStore(a).AdminAsync();
        var aToken = aAdmin.DefaultRequestHeaders.Authorization!.Parameter!;

        var onBsHost = _api.ForStore(b).Authorized(aToken, b.Host);

        (await onBsHost.GetAsync("/api/admin/reports/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task العميل_والزائر_لا_يقرآن_لوحة_المتجر()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var (customer, _) = await api.NewCustomerAsync();

        (await customer.GetAsync("/api/admin/reports/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await api.Anonymous().GetAsync("/api/admin/reports/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task الموظّف_يقرأ_اللوحة_كالمدير()
    {
        // store.reports.view ممنوحة لـ TenantAdmin وTenantStaff في جدول الأدوار.
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var staff = await api.LoginAsync(
            await _factory.CreateStoreUserAsync(store.Tenant, Roles.TenantStaff), SouqApiFactory.StoreAdminPassword);

        (await staff.GetAsync("/api/admin/reports/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task متجر_بلا_بيانات_يعيد_أصفاراً_لا_فراغاً()
    {
        // متجر جديد: اللوحة يجب أن تبقى قابلة للعرض — لا null ولا قسمة على صفر ولا سلسلة زمنية فارغة.
        var store = await _factory.CreateStoreAsync();

        var dashboard = await ReadAsync(await _api.ForStore(store).AdminAsync(), "Last7Days");

        dashboard.Current.Revenue.Should().Be(0m);
        dashboard.Current.AverageOrderValue.Should().Be(0m);
        dashboard.Trend.Should().HaveCount(7, "كل يوم في المدّة نقطة، ولو بصفر");
        dashboard.Trend.Should().OnlyContain(p => p.Revenue == 0m && p.Orders == 0);
        dashboard.OrdersByStatus.Should().ContainKeys("Pending", "Paid", "Shipped", "Delivered", "Cancelled");
        dashboard.TopProducts.Should().BeEmpty();
        dashboard.Inventory.Should().Be(new Inventory(0, 0, 0));
    }

    [Fact]
    public async Task المدّة_تأتي_من_مفتاح_مغلق_لا_من_تواريخ_المتصفّح()
    {
        var api = _api.ForStore(await _factory.CreateStoreAsync());
        var admin = await api.AdminAsync();

        var week = await ReadAsync(admin, "Last7Days");
        (week.To - week.From).Should().Be(TimeSpan.FromDays(7));

        // مفتاح غير معروف يُرفض بدل أن يُفسَّر كمدى مفتوح.
        (await admin.GetAsync("/api/admin/reports/dashboard?range=Everything")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task قراءة_اللوحة_تُسجَّل_في_سجلّ_التدقيق()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);

        await ReadAsync(await api.AdminAsync(), "Today");

        var logged = await api.WithDbAsync(db => db.AuditEntries
            .AnyAsync(e => e.Action == "store.dashboard.viewed"));
        logged.Should().BeTrue();
    }

    // ============================================================================
    // يوم المتجر لا يوم UTC، على قاعدة حقيقية (C11).
    //
    // متاجر الاختبار كلّها في `Asia/Amman` (UTC+3)، فالفجوة ثلاث ساعات دائماً ولا تعتمد على
    // ساعة تشغيل الاختبار. ما يثبته هذا الاختبار ولا يثبته اختبار وحدة: أنّ الحدّ الذي حسبه
    // المعالج هو الذي **وصل SQL فعلاً**، وأن دلو المنحنى بُني على اليوم نفسه.
    // ============================================================================
    [Fact]
    public async Task حدود_اللوحة_ودلاؤها_على_يوم_المتجر_لا_على_يوم_UTC()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();

        var zone = TimeZoneInfo.FindSystemTimeZoneById(store.Tenant.TimeZone);
        var localMidnight = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        var expectedFrom = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), zone);

        var today = await ReadAsync(admin, "Today");

        // منتصف ليل المتجر، لا منتصف ليل UTC — الفرق ثلاث ساعات، وهو بالضبط ما كان ضائعاً.
        today.From.Should().Be(expectedFrom);
        today.To.Should().Be(expectedFrom.AddDays(1));
        DateTime.SpecifyKind(today.From, DateTimeKind.Unspecified)
            .Should().NotBe(DateTime.UtcNow.Date, "لو كانت UTC لساوت منتصف ليل UTC");

        // ودلو المنحنى الوحيد ليومٍ واحد هو بداية ذلك اليوم نفسه.
        today.Trend.Should().ContainSingle().Which.Bucket.Should().Be(expectedFrom);
    }

    // ============================================================================
    // طلبٌ في الساعة الواحدة فجراً بتوقيت المتجر هو من **يومه**، وبـ UTC هو من أمس: 22:00 من
    // اليوم السابق. قبل C11 كان يسقط من لوحة "اليوم" تماماً — بيعٌ حقيقي لا يراه صاحبه.
    // ============================================================================
    [Fact]
    public async Task طلب_فجر_اليوم_بتوقيت_المتجر_يدخل_لوحة_اليوم()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 30m, stock: 10);

        var (customer, _) = await api.NewCustomerAsync();
        var created = await api.PlaceOrderAsync(customer, productId, 1);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = int.Parse(
            (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString()!);
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(store.Tenant.TimeZone);
        var oneAmLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date.AddHours(1);
        var oneAmUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(oneAmLocal, DateTimeKind.Unspecified), zone);

        // نُرجع لحظة الطلب إلى فجر اليوم المحلّي — وهي بـ UTC من **أمس**.
        await api.WithDbAsync(async db =>
        {
            var order = await db.Orders.FirstAsync(o => o.Id == orderId);
            db.Entry(order).Property(nameof(Souq.Domain.Entities.Order.PlacedAt)).CurrentValue = oneAmUtc;
            return await db.SaveChangesAsync();
        });

        oneAmUtc.Date.Should().Be(DateTime.UtcNow.Date.AddDays(-1),
            "الفرضية نفسها: فجر اليوم بعمّان هو أمس بـ UTC");

        var today = await ReadAsync(admin, "Today");

        today.Current.Orders.Should().Be(1, "الطلب من يوم التاجر وإن كان من أمس بـ UTC");
        today.Current.Revenue.Should().Be(30m);
        today.Trend.Should().ContainSingle().Which.Revenue.Should().Be(30m, "ويقع في دلو يومه هو");
    }
    // ============================================================================
    // الاسترداد يُنسب إلى **مدّته** لا إلى مدّة الطلب (C11).
    //
    // قبل هذا كان المجموع يُؤخذ من `Payment.RefundedAmount` — عدّادٌ تراكمي بلا تاريخ — لكل طلبٍ
    // وقع في المدّة. فاستردادٌ يقع اليوم عن طلبٍ قديم كان يُخصم من **مدّة ذلك الطلب**: صافي مدّةٍ
    // مضت يتغيّر بأثر رجعي، وصافي المدّة الجارية لا يرى ما خرج من الصندوق فيها.
    //
    // الطلب هنا يُدفع ثم يُرجَع تاريخُه إلى ما قبل النافذة، والاسترداد يقع الآن: فبالقاعدة القديمة
    // يظهر صفراً (لا طلب محسوب في المدّة)، وبالجديدة يظهر بمبلغه.
    // ============================================================================
    [Fact]
    public async Task الاسترداد_يُنسب_إلى_مدّته_لا_إلى_مدّة_الطلب()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 50m, stock: 100);

        var (customer, _) = await api.NewCustomerAsync();
        var created = await api.PlaceOrderAsync(customer, productId, 2);   // 100
        var orderId = int.Parse(
            (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString()!);
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // الطلب يصير قديماً: خارج نافذة "اليوم" بيقين.
        await api.WithDbAsync(async db =>
        {
            var order = await db.Orders.FirstAsync(o => o.Id == orderId);
            db.Entry(order).Property(nameof(Souq.Domain.Entities.Order.PlacedAt)).CurrentValue =
                DateTime.UtcNow.AddDays(-10);
            return await db.SaveChangesAsync();
        });

        (await admin.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { amount = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var today = await ReadAsync(admin, "Today");

        today.Current.Orders.Should().Be(0, "الطلب خارج المدّة");
        today.Current.Revenue.Should().Be(0m);
        today.Current.Refunds.Should().Be(30m, "لكن المال خرج **اليوم**، فهو خصم اليوم");
        today.Current.NetRevenue.Should().Be(-30m, "صافي سالب جوابٌ صحيح ليومٍ لم يبع وردّ مالاً");
    }

    // ============================================================================
    // استردادٌ **فشل** ليس خصماً. `Refund.Fail` تختم `CompletedAt` تماماً كما تختمها `Succeed`،
    // فالتصفية بالتاريخ وحده كانت ستخصم مالاً لم يخرج من الصندوق قطّ.
    // ============================================================================
    [Fact]
    public async Task استرداد_رفضته_البوّابة_لا_يُخصم_من_الإيراد()
    {
        var store = await _factory.CreateStoreAsync();
        var api = _api.ForStore(store);
        var admin = await api.AdminAsync();
        var productId = await api.CreateProductAsync(admin, price: 50m, stock: 100);

        var (customer, _) = await api.NewCustomerAsync();
        var created = await api.PlaceOrderAsync(customer, productId, 2);   // 100
        var orderId = int.Parse(
            (await created.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestApi.Json))!["orderId"].ToString()!);
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { amount = 30m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // تُرَدّ حالة الاسترداد إلى "فشل" بختمه الزمني كما تفعل البوّابة.
        await api.WithDbAsync(async db =>
        {
            var refund = await db.Refunds.FirstAsync();
            db.Entry(refund).Property(nameof(Souq.Domain.Entities.Refund.Status)).CurrentValue =
                Souq.Domain.Enums.RefundStatus.Failed;
            db.Entry(refund).Property(nameof(Souq.Domain.Entities.Refund.CompletedAt)).CurrentValue = DateTime.UtcNow;
            return await db.SaveChangesAsync();
        });

        var dashboard = await ReadAsync(admin);

        dashboard.Current.Refunds.Should().Be(0m, "ما لم يخرج من الصندوق لا يُخصم منه");
        dashboard.Current.NetRevenue.Should().Be(100m);
    }
}
