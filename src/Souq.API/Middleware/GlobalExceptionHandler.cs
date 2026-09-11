using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Souq.API.Http;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Exceptions;

namespace Souq.API.Middleware;

// ============================================================================
// GlobalExceptionHandler — الترجمة المركزية للاستثناءات إلى عقد الأخطاء (ADR-0017):
//   ValidationException (FluentValidation)       ⇒ 400 ValidationFailed + الحقول
//   DomainException (قاعدة يحرسها كيان)          ⇒ 422 برمز الاستثناء نفسه
//   ConcurrencyConflictException (rowversion)    ⇒ 409 ConcurrencyConflict
//   UniqueConstraintViolationException (قيد فريد) ⇒ 409 DuplicateValue
//   BadHttpRequestException (Kestrel: جسم ضخم…)  ⇒ رمزه نفسه
//   أي شيء آخر                                    ⇒ 500 برسالة عامة؛ التفاصيل في السجل فقط
// لا يعبر للعميل أبداً: نص استثناء غير متوقّع، مكدّس الاستدعاء، أسماء أنواع، تفاصيل SQL.
// ============================================================================
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private const string GenericServerError = "حدث خطأ غير متوقّع في الخادم";
    private const string UnreadableRequest = "تعذّرت قراءة الطلب";

    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails; _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var problem = ToProblem(exception);
        var status = problem.Status!.Value;
        Log(httpContext, exception, status);

        problem.Extensions[ProblemDetailsConventions.TraceIdKey] = RequestCorrelation.GetId(httpContext);
        httpContext.Response.StatusCode = status;
        if (await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext, ProblemDetails = problem, Exception = exception,
            }))
            return true;

        // عميل بترويسة Accept لا تشمل JSON: نكتب العقد نفسه على أي حال بدل 500 فارغ.
        await httpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }

    internal static ProblemDetails ToProblem(Exception exception) => exception switch
    {
        ValidationException validation => ValidationProblem(validation),
        DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
        ConcurrencyConflictException conflict => Problem(StatusCodes.Status409Conflict, "ConcurrencyConflict", conflict.Message),
        UniqueConstraintViolationException duplicate => Problem(StatusCodes.Status409Conflict, "DuplicateValue", duplicate.Message),
        BadHttpRequestException badRequest => Problem(badRequest.StatusCode,
            ProblemDetailsConventions.DefaultCode(badRequest.StatusCode, hasFieldErrors: false), UnreadableRequest),
        _ => Problem(StatusCodes.Status500InternalServerError, "ServerError", GenericServerError),
    };

    private static ProblemDetails Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails { Status = status, Detail = detail };
        problem.Extensions[ProblemDetailsConventions.CodeKey] = code;
        return problem;
    }

    // أسماء الحقول بصيغة JSON التي أرسلها العميل (items[0].quantity لا Items[0].Quantity)
    // كي تربط الواجهة كل رسالة بحقلها مباشرة.
    private static ProblemDetails ValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => ToJsonPath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Detail = string.Join(" | ", exception.Errors.Select(e => e.ErrorMessage).Distinct()),
        };
        problem.Extensions[ProblemDetailsConventions.CodeKey] = "ValidationFailed";
        return problem;
    }

    private static string ToJsonPath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));

    private void Log(HttpContext httpContext, Exception exception, int status)
    {
        if (status >= StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path.Value);
        else if (status == StatusCodes.Status409Conflict)
            _logger.LogWarning("Persistence conflict {ConflictType} on {Method} {Path}",
                exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path.Value);
        else
            _logger.LogDebug("Request rejected with {StatusCode}: {ExceptionType}", status, exception.GetType().Name);
    }
}
