using Microsoft.AspNetCore.Mvc;
using Souq.Application.Common.Models;

namespace Souq.API.Http;

// ============================================================================
// ترجمة فشل Result إلى استجابة HTTP — مكان واحد لكل الـ Controllers بدل ثلاث نسخ
// منسوخة من MapFailure (Phase 0 D5). المرحلة 1B تحوّل الشكل إلى ProblemDetails هنا
// فقط، دون لمس أي Controller.
//   NotFound ⇒ 404 (غير موجود أو يخصّ غيرك — لا نكشف الوجود)
//   Conflict ⇒ 409 (تغيّرت البيانات منذ قراءتها)
//   غير ذلك ⇒ 400 (قاعدة عمل أو مدخل غير صالح)
// ============================================================================
public static class ResultHttpExtensions
{
    public static IActionResult Failure(this ControllerBase controller, string? error, string? code)
    {
        var body = new { error, code };
        return code switch
        {
            "NotFound" => controller.NotFound(body),
            "Conflict" => controller.Conflict(body),
            _ => controller.BadRequest(body),
        };
    }

    public static IActionResult Failure(this ControllerBase controller, Result result)
        => controller.Failure(result.Error, result.ErrorCode);

    public static IActionResult Failure<T>(this ControllerBase controller, Result<T> result)
        => controller.Failure(result.Error, result.ErrorCode);
}
