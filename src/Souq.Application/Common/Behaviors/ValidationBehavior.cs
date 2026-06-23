using FluentValidation;
using MediatR;

namespace Souq.Application.Common.Behaviors;

// ============================================================================
// ValidationBehavior — "سلوك خط أنابيب" (Pipeline Behavior).
// لماذا؟ بدل استدعاء التحقّق يدوياً في كل معالج (تكرار + نسيان محتمل)، نعترض
// كل أمر تلقائياً قبل وصوله لمعالجه ونُشغّل مدقّقاته. يُكتب مرة، يُطبّق على الكل.
// هذا مثال على "الاهتمامات المتقاطعة" (Cross-Cutting Concerns).
// ============================================================================
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = _validators
                .Select(v => v.Validate(context))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Any())
                throw new ValidationException(failures);
        }
        return await next();
    }
}
