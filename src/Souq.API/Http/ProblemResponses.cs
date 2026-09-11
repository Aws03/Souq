using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Souq.API.Http;

// كتابة ProblemDetails من وسيط (خارج الـ Controllers) بالعقد نفسه (ADR-0017): code ثابت، وtraceId
// يضيفه ProblemDetailsConventions.Customize. عميل بترويسة Accept لا تشمل JSON يستلم العقد نفسه.
public static class ProblemResponses
{
    public static async Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        var problem = new ProblemDetails { Status = status, Detail = detail };
        problem.Extensions[ProblemDetailsConventions.CodeKey] = code;
        problem.Extensions[ProblemDetailsConventions.TraceIdKey] = RequestCorrelation.GetId(context);
        context.Response.StatusCode = status;

        var writer = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        if (!await writer.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
            await context.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json");
    }
}
