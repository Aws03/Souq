using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Common;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// مصفوفة الأدوار × النقاط عبر HTTP الحقيقي (ADR-0010): لكل دور ما يسمح به جدول RolePermissions فقط.
// "مسموح" = لا 401 ولا 403 (حالة الاستخدام قد تقول 404/409/422 لأسبابها). زائر ⇒ 401، دور بلا صلاحية ⇒ 403.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class AuthorizationMatrixTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public AuthorizationMatrixTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    // ============================================================================
    // R-03 — من يستطيع أن يُخرِج مالاً؟ **هذا الاختبار يثبّت جواب اليوم، ولا يقترح غيره.**
    //
    // الاسترداد الصريح (POST /api/orders/{id}/refunds) خلف store.payments.manage، ويملكها TenantAdmin وحده.
    // لكنّ إلغاء طلب **مدفوع** عبر PUT /api/orders/{id}/status يسترد ثمنه كاملاً، وهو خلف orders.manage التي
    // يملكها الموظّف أيضاً. فالموظّف يستطيع أن يُخرِج المال من الباب الأول لا الثاني.
    //
    // ليس هذا تصميماً بل تسلسلاً: البوّابة وُضعت يوم كانت النقطة تنقل الحالات ولا تمسّ مالاً، ثمّ أُضيف
    // الاسترداد إلى المعالج نفسه في المرحلة 11. **هل يبقى كذلك؟ قرار المالك (R-03)، لا قرار هذا الاختبار.**
    //
    // ولماذا يُكتب الآن (M6): بلا هذا التثبيت، إضافةُ حارس store.payments.manage داخل المعالج — أي تنفيذُ
    // جواب "نعم" — تُبقي المجموعة كلّها خضراء: لا سمة تتغيّر فجرد النقاط المولَّد لا يتحرّك، وكلّ من يستدعي
    // /status في الاختبارات مديرٌ يملك الصلاحيتين. فكان أخطر ما في R-03 أنّ سلوكه غير مثبَّت أصلاً: يُقلَب
    // عمداً أو سهواً بلا اختبار أحمر واحد. الآن يسقط هذا الاختبار، فيُقرأ ويُحدَّث بقرار صريح.
    // ============================================================================
    [Fact]
    public async Task الموظّف_يستطيع_اليوم_أن_يسترد_بإلغاء_طلب_مدفوع_وهو_ما_يسأل_عنه_R03()
    {
        var tenant = await _factory.DefaultTenantAsync();
        var admin = await _api.AdminAsync();
        var staff = await _api.LoginAsync(
            await _factory.CreateStoreUserAsync(tenant, Roles.TenantStaff), SouqApiFactory.StoreAdminPassword);

        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 2);
        var (customer, _) = await _api.NewCustomerAsync();
        var placed = await _api.PlaceOrderAsync(customer, productId, 1);
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await customer.PostAsync($"/api/orders/{orderId}/confirm-payment", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // الباب المغلق أمام الموظّف: طلب استرداد صريح.
        (await staff.PostAsJsonAsync($"/api/orders/{orderId}/refunds", new { amount = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "الاسترداد الصريح خلف store.payments.manage ويملكها المدير وحده");

        // والباب المفتوح: إلغاء الطلب المدفوع — وهو يسترد ثمنه كاملاً.
        var cancelled = await staff.PutAsJsonAsync($"/api/orders/{orderId}/status", new { action = "Cancel" });
        cancelled.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "orders.manage وحدها تكفي اليوم لإلغاء طلب مدفوع — وهذا هو سؤال R-03 بعينه");
        cancelled.IsSuccessStatusCode.Should().BeTrue(await cancelled.Content.ReadAsStringAsync());

        // والمال خرج فعلاً: الإلغاء استرد، لا أنّه غيّر حالة وحدها.
        var refunded = await _api.WithDbAsync(async db =>
            await db.Payments.Where(p => p.OrderId == orderId).Select(p => p.RefundedAmount).SingleAsync());
        refunded.Should().BeGreaterThan(0m, "إلغاء طلب مدفوع يسترد ثمنه — فالموظّف أخرج مالاً بصلاحية الطلبات وحدها");
    }

    [Fact]
    public async Task كل_دور_يصل_لما_يسمح_به_جدول_الصلاحيات_فقط()
    {
        var tenant = await _factory.DefaultTenantAsync();
        var admin = await _api.AdminAsync();
        var staff = await _api.LoginAsync(
            await _factory.CreateStoreUserAsync(tenant, Roles.TenantStaff), SouqApiFactory.StoreAdminPassword);
        var (customer, _) = await _api.NewCustomerAsync();
        var actors = new (string Role, HttpClient Client)[]
        {
            (Roles.TenantAdmin, admin), (Roles.TenantStaff, staff), (Roles.Customer, customer), ("Anonymous", _api.Anonymous()),
        };

        var cases = new (string Method, string Url, Func<object?> Body, string[] Allowed)[]
        {
            ("POST", "/api/categories", () => TestApi.CategoryBody($"mx-{Guid.NewGuid():N}"[..20], "مصفوفة"),
                [Roles.TenantAdmin, Roles.TenantStaff]),
            ("GET", "/api/orders", () => null, [Roles.TenantAdmin, Roles.TenantStaff]),
            ("GET", "/api/admin/inventory", () => null, [Roles.TenantAdmin, Roles.TenantStaff]),
            ("POST", "/api/coupons", () => new { code = $"MX{Guid.NewGuid():N}"[..12], type = "Percentage", value = 5m },
                [Roles.TenantAdmin]),
            ("GET", "/api/coupons", () => null, [Roles.TenantAdmin]),
            ("GET", "/api/auth/me", () => null, [Roles.TenantAdmin, Roles.TenantStaff, Roles.Customer]),
        };

        foreach (var (method, url, body, allowed) in cases)
        foreach (var (role, client) in actors)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (body() is { } payload) request.Content = JsonContent.Create(payload, options: TestApi.Json);
            var status = (await client.SendAsync(request)).StatusCode;

            var expectation = $"{role} → {method} {url}";
            if (allowed.Contains(role))
                status.Should().NotBe(HttpStatusCode.Unauthorized, expectation).And.NotBe(HttpStatusCode.Forbidden, expectation);
            else
                status.Should().Be(role == "Anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, expectation);
        }
    }

    [Fact]
    public async Task الموظّف_بلا_ملف_عميل_لا_يشتري_ولا_يرى_طلبات_شخصية()
    {
        var tenant = await _factory.DefaultTenantAsync();
        var staff = await _api.LoginAsync(
            await _factory.CreateStoreUserAsync(tenant, Roles.TenantStaff), SouqApiFactory.StoreAdminPassword);
        var productId = await _api.CreateProductAsync(await _api.AdminAsync());

        var order = await _api.PlaceOrderAsync(staff, productId, 1);
        order.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await order.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("CustomerAccountRequired");

        (await staff.GetAsync("/api/orders/mine")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
