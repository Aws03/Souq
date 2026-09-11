using System.Text.Json;
using FluentValidation;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Exceptions;

namespace Souq.API.Middleware;

// ============================================================================
// ExceptionHandlingMiddleware — معالجة الأخطاء مركزياً (Cross-Cutting). نعترض أي
// استثناء يصعد من أي مكان ونترجمه لاستجابة JSON موحّدة برمز HTTP صحيح:
//   مدخل غير صالح (FluentValidation)            ⇒ 400
//   قاعدة عمل (DomainException، بما فيها المال)  ⇒ 400
//   تعارض حفظ متزامن أو قيمة فريدة مكرّرة        ⇒ 409 (ADR-0013)
//   غير متوقّع                                     ⇒ 500 برسالة عامة (التفاصيل في السجل فقط)
// المرحلة 1B تحوّل الشكل إلى RFC 7807 ProblemDetails.
// ============================================================================
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context); }
        catch (Exception ex)
        {
            var (status, message, code) = ex switch
            {
                ValidationException ve => (400, string.Join(" | ", ve.Errors.Select(e => e.ErrorMessage)), "ValidationFailed"),
                DomainException de => (400, de.Message, "BusinessRule"),
                ConcurrencyConflictException ce => (409, ce.Message, "Conflict"),
                UniqueConstraintViolationException ue => (409, ue.Message, "Conflict"),
                _ => (500, "حدث خطأ غير متوقّع في الخادم", "ServerError"),
            };

            if (status == 500) _logger.LogError(ex, "خطأ غير متوقّع");
            else if (status == 409) _logger.LogWarning("تعارض حفظ: {ConflictType}", ex.GetType().Name);

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message, code }));
        }
    }
}
