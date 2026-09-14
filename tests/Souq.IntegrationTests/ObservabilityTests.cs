using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الرصد (ADR-0018) عبر HTTP الحقيقي: معرّف ربط واحد في الترويسة وجسم الخطأ ونطاق السجل؛ سطر
// لكل طلب بلا سلسلة الاستعلام؛ سياق المستخدم وحالة الاستخدام يرثه كل سجلّ داخل الطلب (حتى
// أوامر SQL)؛ ولا سرّ في أي سجل.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class ObservabilityTests
{
    private const string RequestLogCategory = "Souq.API.Observability.RequestLoggingMiddleware";
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public ObservabilityTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task كل_استجابة_تحمل_معرّف_ربط_W3C_يطابق_traceId_في_جسم_الخطأ()
    {
        var notFound = await _api.Anonymous().GetAsync("/api/products/987654321");
        var ok = await _api.Anonymous().GetAsync("/api/categories");

        var correlationId = notFound.Headers.GetValues(CorrelationHeader).Single();
        correlationId.Should().MatchRegex("^[0-9a-f]{32}$");
        (await notFound.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.TraceId.Should().Be(correlationId);
        ok.Headers.GetValues(CorrelationHeader).Single().Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe(correlationId);
    }

    [Fact]
    public async Task سطر_سجلّ_واحد_لكل_طلب_بالمسار_والقالب_والرمز_والمدّة_بلا_سلسلة_الاستعلام()
    {
        var response = await _api.Anonymous().GetAsync("/api/products?keyword=query-secret-value&page=1");
        var correlationId = response.Headers.GetValues(CorrelationHeader).Single();

        var line = RequestLines(correlationId).Should().ContainSingle().Subject;
        line.Properties["Method"].Should().Be("GET");
        line.Properties["Path"].Should().Be("/api/products");
        line.Properties["StatusCode"].Should().Be(200);
        line.Properties["Route"].Should().Be("api/Products");
        line.Properties["ElapsedMs"].Should().BeOfType<double>();
        line.Message.Should().NotContain("query-secret-value");
    }

    [Fact]
    public async Task الطلب_المُصادَق_ينقل_UserId_وحالة_الاستخدام_إلى_كل_سجلّ_داخله_حتى_أوامر_SQL()
    {
        var (customer, email) = await _api.NewCustomerAsync();
        var userId = await _api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        var response = await customer.GetAsync("/api/orders/mine");
        var correlationId = response.Headers.GetValues(CorrelationHeader).Single();

        RequestLines(correlationId).Should().ContainSingle().Which.Scope["UserId"].Should().Be(userId);
        var sqlInsideUseCase = _factory.Logs.Entries
            .Where(e => e.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal)
                        && Equals(e.Scope.GetValueOrDefault("CorrelationId"), correlationId))
            .ToList();
        sqlInsideUseCase.Should().NotBeEmpty()
            .And.OnlyContain(e => Equals(e.Scope["UserId"], userId) && Equals(e.Scope["UseCase"], "GetMyOrdersQuery"));
    }

    // R-10: الرمز جزء من المسار، فكان سطر الطلب يكتبه كاملاً — ومن يقرأ السجلّ يفتح صفحة تتبّع أي عميل.
    [Fact]
    public async Task رمز_تتبّع_الطلب_يُنقَّح_من_سطر_السجلّ_ويبقى_القالب_للتشخيص()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 5m, stock: 2);
        var (customer, _) = await _api.NewCustomerAsync();
        var placed = await _api.PlaceOrderAsync(customer, productId, 1);
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        var token = await _api.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.TrackingToken).SingleAsync());

        var response = await _api.Anonymous().GetAsync($"/api/orders/track/{token}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var correlationId = response.Headers.GetValues(CorrelationHeader).Single();

        var line = RequestLines(correlationId).Should().ContainSingle().Subject;
        line.Properties["Path"].Should().Be("/api/orders/track/***");
        line.Properties["Route"].Should().Be("api/Orders/track/{token}", "القالب يبقى كما هو للتجميع والتشخيص");
        _factory.Logs.Entries.Should().NotContain(e => e.Message.Contains(token), "الرمز لا يظهر في أي سجلّ");
    }

    [Fact]
    public async Task لا_كلمة_مرور_ولا_توكن_ولا_ترويسة_تفويض_في_أي_سجل()
    {
        var (customer, email) = await _api.NewCustomerAsync();        // كلمة المرور: Customer-Pass-1
        var jwt = customer.DefaultRequestHeaders.Authorization!.Parameter!;
        await _api.LoginAsync(email, "Customer-Pass-1");
        await customer.GetAsync("/api/orders/mine");
        (await customer.GetAsync("/api/admin/inventory")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var everything = _factory.Logs.Entries
            .SelectMany(e => new[] { e.Message }
                .Concat(e.Properties.Values.Select(v => v?.ToString()))
                .Concat(e.Scope.Values.Select(v => v?.ToString())))
            .OfType<string>()
            .ToList();

        everything.Should().NotContain(s => s.Contains("Customer-Pass-1"));
        everything.Should().NotContain(s => s.Contains(jwt));
        everything.Should().NotContain(s => s.Contains("Bearer ", StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<CapturedLog> RequestLines(string correlationId) =>
        _factory.Logs.Entries.Where(e => e.Category == RequestLogCategory
                                         && Equals(e.Scope.GetValueOrDefault("CorrelationId"), correlationId));

    // ── M10: التنقيح قبل التوجيه ────────────────────────────────────────────────────
    // Redact يقرأ RouteValues، وهي لا تُملأ إلا بعد UseRouting. استثناء في وسيط مبكّر (تعثّر
    // القاعدة أثناء تحديد المستأجر مثلاً) يصل لمعالج الاستثناءات بمسار خام يحمل الرمز كاملاً.
    [Fact]
    public void رمز_التتبّع_يُنقَّح_حتى_قبل_أن_يُحسب_التوجيه()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Path = "/api/orders/track/9f3ca1d24b7e4f0a8c5d6e7f80123456";

        var redacted = Souq.API.Observability.SensitivePath.Redact(context);

        redacted.Should().Be("/api/orders/track/***");
        redacted.Should().NotContain("9f3ca1d2");
    }

    [Fact]
    public void المسارات_العادية_لا_تُنقَّح_فيبقى_السجلّ_مفيداً()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Path = "/api/products/42";

        Souq.API.Observability.SensitivePath.Redact(context).Should().Be("/api/products/42");
    }

    [Fact]
    public void قيمة_المسار_من_التوجيه_تُنقَّح_أينما_وردت()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Path = "/api/orders/track/secret-token-value";
        context.Request.RouteValues["token"] = "secret-token-value";

        Souq.API.Observability.SensitivePath.Redact(context).Should().Be("/api/orders/track/***");
    }
}
