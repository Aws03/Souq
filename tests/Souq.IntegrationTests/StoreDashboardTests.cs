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
}
