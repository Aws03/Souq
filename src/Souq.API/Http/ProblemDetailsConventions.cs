using Microsoft.AspNetCore.Http;
using Souq.Application.Common.Models;

namespace Souq.API.Http;

// ============================================================================
// عقد الأخطاء (ADR-0017): كل استجابة خطأ من الـ API هي RFC 7807 ProblemDetails
// (application/problem+json) مع حقلين ثابتين إضافيين:
//   code    — رمز ثابت تترجمه الواجهة. هو العقد؛ الرسالة (detail) للقراءة وقد تتغيّر.
//   traceId — معرّف الطلب نفسه في سجلات الخادم (يطلبه الدعم الفني من المستخدم).
// هذا الملف المكان الوحيد الذي يقرّر: أي نوع خطأ ⇒ أي رمز HTTP، وأي رمز افتراضي.
// ============================================================================
public static class ProblemDetailsConventions
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    // رمز افتراضي لأخطاء لا يُنتجها كودنا: 401/403 من المصادقة، 404 لمسار مجهول، 400 من
    // ربط JSON في [ApiController]، 413 من Kestrel.
    public static string DefaultCode(int? status, bool hasFieldErrors) => status switch
    {
        400 => hasFieldErrors ? "ValidationFailed" : "BadRequest",
        401 => "Unauthenticated",
        403 => "Forbidden",
        404 => "NotFound",
        405 => "MethodNotAllowed",
        409 => "Conflict",
        413 => "PayloadTooLarge",
        415 => "UnsupportedMediaType",
        422 => "BusinessRule",
        429 => "TooManyRequests",
        503 => "ServiceUnavailable",
        >= 500 => "ServerError",
        _ => "Error",
    };

    // يُطبَّق على كل ProblemDetails يكتبه الإطار أو كودنا (مسجَّل في AddProblemDetails):
    // traceId دائماً، وcode إن لم يحدّده المنتِج.
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Extensions[TraceIdKey] = RequestCorrelation.GetId(context.HttpContext);
        if (!problem.Extensions.ContainsKey(CodeKey))
            problem.Extensions[CodeKey] = DefaultCode(
                problem.Status, problem is HttpValidationProblemDetails { Errors.Count: > 0 });
    }
}
