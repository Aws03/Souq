using System.Text.Json;
using AwesomeAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Souq.API.Http;
using Souq.API.Middleware;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Exceptions;

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

    private static async Task<(int Status, JsonElement Json)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsConventions.Customize)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(), NullLogger<GlobalExceptionHandler>.Instance);
        (await handler.TryHandleAsync(context, exception, CancellationToken.None)).Should().BeTrue();

        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }
}
