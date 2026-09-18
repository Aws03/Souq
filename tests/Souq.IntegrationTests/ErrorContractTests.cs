using System.Net;
using System.Net.Http.Json;
using System.Text;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// عقد الأخطاء عبر HTTP الحقيقي (ADR-0017): كل خطأ — من كودنا أو من الإطار (المصادقة،
// التوجيه، ربط JSON) — RFC 7807 ProblemDetails بـ code ثابت وtraceId، ولا يكشف أبداً
// أسماء أنواعنا الداخلية أو مكدّس الاستدعاء.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ErrorContractTests
{
    private readonly TestApi _api;

    public ErrorContractTests(SouqApiFactory factory) => _api = new TestApi(factory);

    [Fact]
    public async Task أخطاء_التحقّق_400_بحقول_JSON_ورمز_ValidationFailed()
    {
        var response = await _api.Anonymous().PostAsJsonAsync("/api/auth/register",
            new { fullName = "", email = "not-an-email", password = "short" });

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "ValidationFailed");
        problem.Errors.Should().NotBeNull();
        problem.Errors!.Keys.Should().Contain(["fullName", "email", "password"]);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"action\":\"Explode\"}")]
    public async Task جسم_JSON_تالف_أو_قيمة_enum_مجهولة_400_بلا_تسريب_تفاصيل_داخلية(string body)
    {
        var admin = await _api.AdminAsync();

        var response = await admin.PutAsync("/api/orders/1/status", new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "ValidationFailed");
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("Souq.").And.NotContain("System.").And.NotContain("Exception").And.NotContain(" at ");
    }

    // F-16: الجسم الفارغ {} كان جسماً "موجوداً"، فيملأ الربطُ الأعضاءَ بأصفارها وينفَّذ الأمر بجواب ناجح —
    // ProductStatus.Draft يُخفي المنتج، وAutoApprove=false يفرض المراجعة اليدوية. IsInEnum لا يكشف ذلك: صفر عضو معرّف.
    [Fact]
    public async Task جسم_فارغ_يُرفض_بدل_أن_يُنفَّذ_بالعضو_صفر()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin);

        await AssertProblemAsync(await admin.PutAsJsonAsync($"/api/admin/products/{productId}/status", new { }),
            HttpStatusCode.BadRequest, "ValidationFailed");
        await AssertProblemAsync(await admin.PutAsJsonAsync("/api/admin/reviews/settings", new { }),
            HttpStatusCode.BadRequest, "ValidationFailed");

        // التشديد لم يكسر النقطتين: الجسم الصريح ما يزال يعمل.
        (await admin.PutAsJsonAsync($"/api/admin/products/{productId}/status", new { status = "Draft" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync("/api/admin/reviews/settings", new { autoApprove = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task بلا_توكن_401_Unauthenticated()
    {
        var response = await _api.Anonymous().GetAsync("/api/orders/mine");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Unauthenticated");
    }

    [Fact]
    public async Task بيانات_دخول_خاطئة_401_InvalidCredentials()
    {
        var response = await _api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@souq.test", password = "Wrong-Pass-1" });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "InvalidCredentials");
    }

    [Fact]
    public async Task عميل_على_نقطة_إدارية_403_Forbidden()
    {
        var (customer, _) = await _api.NewCustomerAsync();

        var response = await customer.GetAsync("/api/admin/inventory");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Forbidden");
    }

    [Fact]
    public async Task مورد_غير_موجود_ومسار_مجهول_404_NotFound()
    {
        await AssertProblemAsync(await _api.Anonymous().GetAsync("/api/products/987654321"), HttpStatusCode.NotFound, "NotFound");
        await AssertProblemAsync(await _api.Anonymous().GetAsync("/api/no-such-endpoint"), HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task قيمة_مكرّرة_409_برمزها()
    {
        var admin = await _api.AdminAsync();
        var slug = $"it-{Guid.NewGuid():N}"[..20];
        (await admin.PostAsJsonAsync("/api/categories", TestApi.CategoryBody(slug, "فئة"))).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await admin.PostAsJsonAsync("/api/categories", TestApi.CategoryBody(slug, "فئة ٢"));

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "SlugTaken");
    }

    [Fact]
    public async Task قاعدة_عمل_422_برمزها_ورسالة_مقروءة()
    {
        // المطلوب هنا **شكل** مشكلة 422 لقاعدة عمل، لا الكوبونات. كانت العيّنة نقطةَ معاينة عامة حُذفت في M8
        // (TD-06)، فصارت العيّنة نفسَ القاعدة عبر المُقيِّم الباقي: إنشاء طلب برمز كوبون غير موجود.
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 3);
        var (customer, _) = await _api.NewCustomerAsync();

        var response = await customer.PostAsJsonAsync("/api/orders", new
        {
            shippingAddress = "عمّان — عنوان اختبار",
            items = new[] { new { productId, quantity = 1 } },
            couponCode = "NO-SUCH-CODE",
        });

        var problem = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "CouponNotFound");
        problem.Detail.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<TestApi.ProblemBody> AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!;
        problem.Status.Should().Be((int)status);
        problem.Code.Should().Be(code);
        problem.TraceId.Should().NotBeNullOrWhiteSpace();
        problem.Title.Should().NotBeNullOrWhiteSpace();
        return problem;
    }
}
