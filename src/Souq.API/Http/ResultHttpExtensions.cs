using Microsoft.AspNetCore.Mvc;
using Souq.Application.Common.Models;

namespace Souq.API.Http;

// ============================================================================
// ترجمة فشل Result إلى ProblemDetails — مكان واحد لكل الـ Controllers (Phase 0 D5).
// نوع الخطأ (ErrorKind) يحدّد رمز HTTP عبر ProblemDetailsConventions؛ لا Controller
// يطابق نصوص الرموز بنفسه بعد الآن. الرمز (code) يُمرَّر كما هو: عقد ثابت للواجهة.
// ============================================================================
public static class ResultHttpExtensions
{
    public static IActionResult Failure(this ControllerBase controller, Error error)
    {
        var status = ProblemDetailsConventions.StatusFor(error.Kind);
        // المصنع يطبّق نفس التخصيص (traceId، الحقول القياسية) كأي ProblemDetails آخر.
        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext, statusCode: status, detail: error.Message);
        problem.Extensions[ProblemDetailsConventions.CodeKey] = error.Code;
        return new ObjectResult(problem) { StatusCode = status };
    }

    public static IActionResult Failure(this ControllerBase controller, Result result)
        => controller.Failure(result.Error ?? throw NotAFailure());

    public static IActionResult Failure<T>(this ControllerBase controller, Result<T> result)
        => controller.Failure(result.Error ?? throw NotAFailure());

    private static InvalidOperationException NotAFailure() =>
        new("Failure() يُستدعى لنتيجة فاشلة فقط — النتيجة ناجحة.");
}
