using System.Text.Json;
using AwesomeAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Souq.API.Http;
using Souq.API.Middleware;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Exceptions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ترجمة الاستثناءات إلى عقد الأخطاء بمعزل عن HTTP الكامل: حالات يصعب إحداثها عبر
// الـ API بحتمية (تعارض rowversion، قيد فريد، استثناء غير متوقّع) تُثبَت هنا مباشرة.
public class GlobalExceptionHandlerTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task كل_استثناء_يُترجم_لرمز_HTTP_ورمز_عقد_ثابتين(Exception exception, int status, string code)
    {
        var (httpStatus, json) = await HandleAsync(exception);

        httpStatus.Should().Be(status);
        json.GetProperty("status").GetInt32().Should().Be(status);
        json.GetProperty("code").GetString().Should().Be(code);
        json.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    public static TheoryData<Exception, int, string> Cases => new()
    {
        { new ConcurrencyConflictException(), 409, "ConcurrencyConflict" },
        { new UniqueConstraintViolationException(), 409, "DuplicateValue" },
        { new InsufficientStockException("سماعات", 5, 1), 422, "InsufficientStock" },
        { new InvalidMoneyException("x"), 422, "InvalidMoney" },
        { new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge), 413, "PayloadTooLarge" },
        { new InvalidOperationException("boom"), 500, "ServerError" },
    };

    [Fact]
    public async Task أخطاء_التحقّق_تحمل_الحقول_بصيغة_JSON()
    {
        var exception = new ValidationException([
            new ValidationFailure("Items[0].Quantity", "الكمية يجب أن تكون موجبة"),
            new ValidationFailure("ShippingAddress", "العنوان مطلوب"),
        ]);

        var (status, json) = await HandleAsync(exception);

        status.Should().Be(400);
        json.GetProperty("code").GetString().Should().Be("ValidationFailed");
        var errors = json.GetProperty("errors");
        errors.GetProperty("items[0].quantity")[0].GetString().Should().Be("الكمية يجب أن تكون موجبة");
        errors.GetProperty("shippingAddress")[0].GetString().Should().Be("العنوان مطلوب");
    }

    [Fact]
    public async Task الاستثناء_غير_المتوقّع_لا_يكشف_رسالته_ولا_مكدّسه_ولا_نوعه()
    {
        var secret = "Server=prod-db;Password=hunter2";
        Exception exception;
        try { throw new InvalidOperationException(secret); }
        catch (InvalidOperationException ex) { exception = ex; } // مكدّس حقيقي

        var (_, json) = await HandleAsync(exception);

        var raw = json.GetRawText();
        raw.Should().NotContain("hunter2").And.NotContain("InvalidOperationException").And.NotContain(" at ");
        json.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    // العميل يغلق الاتصال باستمرار (تنقّل، إغلاق تبويب، شبكة جوّال): بلا تمييزه يصير كل انقطاع سطرَ ERROR بمكدّسه
    // ومحاولةَ كتابة 500 على اتصال مغلق — فيخفي معدّلُ الأخطاء الأعطالَ الحقيقية.
    [Fact]
    public async Task طلب_ألغاه_العميل_ليس_خطأ_خادم_ولا_يُكتب_له_جواب()
    {
        var logs = new CapturingLoggerProvider();
        var context = NewContext();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;

        var handled = await HandlerFor(context, logs)
            .TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(StatusCodes.Status500InternalServerError);
        context.Response.Body.Length.Should().Be(0, "لا أحد ينتظر جواباً على اتصال أغلقه صاحبه");
        logs.Entries.Should().NotContain(e => e.Level == LogLevel.Error, "الانقطاع ليس عطلاً في الخادم");
    }

    // R-10 مرّة أخرى: التنقيح كان في سطر الطلب وحده، ومسار الخطأ ينسخ المسار خاماً — فيتسرّب الرمز من طريق الخطأ.
    [Fact]
    public async Task رمز_التتبّع_يُنقَّح_من_سجلّ_معالج_الاستثناءات_أيضاً()
    {
        var logs = new CapturingLoggerProvider();
        const string token = "0123456789abcdef0123456789abcdef";
        var context = NewContext();
        context.Request.Path = $"/api/orders/track/{token}";
        context.Request.RouteValues["token"] = token;

        await HandlerFor(context, logs).TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        logs.Entries.Should().NotBeEmpty()
            .And.NotContain(e => e.Message.Contains(token), "من يقرأ السجلّ يفتح صفحة تتبّع صاحب الرمز بلا مصادقة");
        logs.Entries.Should().Contain(e => e.Message.Contains("/api/orders/track/***"), "القالب يبقى مفيداً للتشخيص");
    }

    // ========================================================================
    // السطر الذي يحمل المكدَّس يحمل معرّف الربط أيضاً.
    //
    // هذه الطبقة **خارج** `RequestLoggingMiddleware`، فنطاقُ سجلّ الطلب — وفيه `CorrelationId` —
    // يُتخلَّص منه قبل أن يصل الاستثناءُ إلى هنا. فكان الجسمُ يُسلّم العميلَ `traceId` والسطرُ
    // الوحيد ذو المكدَّس لا يحمله: يشتكي مستخدمٌ برمزه، فلا يُجاب إلا تخميناً بين أخطاءٍ
    // متجاورة زمنياً. والقيمة واحدة في الثلاثة: الترويسة، والجسم، والسجلّ.
    // ========================================================================
    [Fact]
    public async Task سطر_الخطأ_يحمل_معرّف_الربط_نفسه_الذي_يتسلّمه_العميل()
    {
        var logs = new CapturingLoggerProvider();
        var context = NewContext();

        await HandlerFor(context, logs).TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var traceId = document.RootElement.GetProperty(ProblemDetailsConventions.TraceIdKey).GetString()!;

        traceId.Should().NotBeNullOrWhiteSpace();
        logs.Entries.Should().Contain(
            e => e.Level == LogLevel.Error && e.Properties.ContainsKey("CorrelationId")
                 && (string?)e.Properties["CorrelationId"] == traceId,
            "سؤال «ماذا جرى في الطلب الذي رمزه كذا؟» لا يُجاب إن لم يحمله سطرُ الاستثناء");
    }

    private static async Task<(int Status, JsonElement Json)> HandleAsync(Exception exception)
    {
        var context = NewContext();

        (await HandlerFor(context).TryHandleAsync(context, exception, CancellationToken.None)).Should().BeTrue();

        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsConventions.Customize)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static GlobalExceptionHandler HandlerFor(HttpContext context, CapturingLoggerProvider? logs = null)
    {
        ILogger<GlobalExceptionHandler> logger = logs is null
            ? NullLogger<GlobalExceptionHandler>.Instance
            : LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<GlobalExceptionHandler>();
        return new GlobalExceptionHandler(
            context.RequestServices.GetRequiredService<IProblemDetailsService>(), logger);
    }
}
