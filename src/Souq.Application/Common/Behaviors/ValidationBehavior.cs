using FluentValidation;
using MediatR;

namespace Souq.Application.Common.Behaviors;

// ============================================================================
// ValidationBehavior — "سلوك خط أنابيب" (Pipeline Behavior).
// لماذا؟ بدل استدعاء التحقّق يدوياً في كل معالج (تكرار + نسيان محتمل)، نعترض
// كل أمر واستعلام تلقائياً قبل وصوله لمعالجه ونُشغّل مدقّقاته. يُكتب مرة، يُطبّق على
// الكل. هذا مثال على "الاهتمامات المتقاطعة" (Cross-Cutting Concerns).
//
// ValidateAsync لا Validate: قاعدة غير متزامنة (MustAsync) كانت سترمي مع Validate.
// الفشل يرتفع ValidationException ⇒ 400 ProblemDetails بحقوله (ADR-0017).
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
            var failures = new List<FluentValidation.Results.ValidationFailure>();
            foreach (var validator in _validators)
                failures.AddRange((await validator.ValidateAsync(context, ct)).Errors.Where(f => f is not null));

            if (failures.Count > 0)
                throw new ValidationException(failures);
        }
        return await next();
    }
}
