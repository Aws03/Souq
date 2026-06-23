using System.Text.Json;
using FluentValidation;
using Souq.Domain.Exceptions;

namespace Souq.API.Middleware;

// ============================================================================
// ExceptionHandlingMiddleware — معالجة الأخطاء مركزياً (Cross-Cutting).
// لماذا؟ بدل كتابة try/catch في كل Controller (تكرار هائل)، نعترض أي استثناء
// يصعد من أي مكان، ونترجمه لاستجابة JSON موحّدة برمز HTTP صحيح. مكان واحد
// يحكم شكل كل الأخطاء في النظام كله. هذا يجعل واجهة الـ API متّسقة ومتوقّعة.
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
            // نختار رمز HTTP حسب نوع الخطأ: متوقّع (400) أم مفاجئ (500).
            var (status, message) = ex switch
            {
                ValidationException ve => (400, string.Join(" | ", ve.Errors.Select(e => e.ErrorMessage))),
                DomainException de     => (400, de.Message),
                _                      => (500, "حدث خطأ غير متوقّع في الخادم")
            };

            if (status == 500) _logger.LogError(ex, "خطأ غير متوقّع");

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
        }
    }
}
