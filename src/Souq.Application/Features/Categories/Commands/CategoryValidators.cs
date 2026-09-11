using FluentValidation;
using Souq.Application.Features.Products.Queries;
using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Categories.Commands;

// الـ slug: أحرف لاتينية صغيرة وأرقام وشرطات فقط (معرّف رابط نظيف مثل "home-decor").
internal static class SlugRule
{
    public const string Pattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";
    public const string Message = "المُعرّف يقبل أحرفاً لاتينية صغيرة وأرقاماً وشرطات فقط";
}

internal static class CategoryTextRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, IReadOnlyDictionary<string, CatalogTextInput>> texts)
    {
        validator.RuleFor(x => texts(x)).NotEmpty().WithName("translations");
        validator.RuleForEach(x => texts(x)).ChildRules(text =>
        {
            text.RuleFor(t => t.Value).NotNull();
            text.RuleFor(t => t.Value.Name).NotEmpty().MaximumLength(CatalogText.NameMaxLength).When(t => t.Value is not null);
        }).OverridePropertyName("translations");
    }
}

public class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(Category.SlugMaxLength)
            .Matches(SlugRule.Pattern).WithMessage(SlugRule.Message);
        CategoryTextRules.Apply(this, x => x.Translations);
        RuleFor(x => x.ParentId).GreaterThan(0).When(x => x.ParentId.HasValue);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(Category.SlugMaxLength)
            .Matches(SlugRule.Pattern).WithMessage(SlugRule.Message);
        CategoryTextRules.Apply(this, x => x.Translations);
        RuleFor(x => x.ParentId).GreaterThan(0).When(x => x.ParentId.HasValue);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class DeleteCategoryValidator : AbstractValidator<DeleteCategoryCommand>
{
    public DeleteCategoryValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
