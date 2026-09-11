using MediatR;
using Microsoft.Extensions.Logging;

namespace Souq.Application.Common.Behaviors;

// ============================================================================
// UseCaseLoggingBehavior — يفتح نطاق سجلّ باسم حالة الاستخدام (UseCase=CreateOrderCommand) فيرث
// كل سجلّ داخلها هذا السياق، ويحذّر من حالة استخدام بطيئة باسمها ومدّتها (ADR-0018).
// لا يسجّل محتوى الطلب أبداً: الأوامر تحمل كلمات مرور ورموز إعادة تعيين وعناوين (Security.md §9).
// الأول في خط الأنابيب كي يشمل زمن التحقّق أيضاً.
// ============================================================================
public sealed class UseCaseLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(500);

    private readonly ILogger _logger;
    private readonly TimeProvider _clock;

    public UseCaseLoggingBehavior(ILoggerFactory loggers, TimeProvider clock)
    {
        _logger = loggers.CreateLogger("Souq.Application.UseCase");
        _clock = clock;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var useCase = typeof(TRequest).Name;
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["UseCase"] = useCase });

        var started = _clock.GetTimestamp();
        var response = await next();
        var elapsed = _clock.GetElapsedTime(started);

        if (elapsed > SlowThreshold)
            _logger.LogWarning("Slow use case {UseCase} took {ElapsedMs:0} ms", useCase, elapsed.TotalMilliseconds);

        return response;
    }
}
