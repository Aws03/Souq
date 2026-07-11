using FluentValidation;

namespace Souq.Application.Features.Categories.Commands;

// الـ slug: أحرف لاتينية صغيرة وأرقام وشرطات فقط (معرّف رابط نظيف مثل "home-decor").
internal static class SlugRule
{
    public const string Pattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";
    public const string Message = "المُعرّف يقبل أحرفاً لاتينية صغيرة وأرقاماً وشرطات فقط";
}

public class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100)
            .Matches(SlugRule.Pattern).WithMessage(SlugRule.Message);
        RuleFor(x => x.ParentId).GreaterThan(0).When(x => x.ParentId.HasValue);
    }
}

public class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100)
            .Matches(SlugRule.Pattern).WithMessage(SlugRule.Message);
        RuleFor(x => x.ParentId).GreaterThan(0).When(x => x.ParentId.HasValue);
    }
}

public class DeleteCategoryValidator : AbstractValidator<DeleteCategoryCommand>
{
    public DeleteCategoryValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
