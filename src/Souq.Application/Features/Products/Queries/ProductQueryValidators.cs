using FluentValidation;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Queries;

// الاستعلامات مُدخلات عامة (زوّار) مثل الأوامر تماماً — ValidationBehavior يطبّق
// هذه القواعد تلقائياً قبل المعالج، فمدخل غير صالح يعود 400 بدل خطأ SQL (500).
// قواعد الصفحة موروثة من PagedQueryValidator (مصدر واحد لكل القوائم).
public class GetProductsQueryValidator : PagedQueryValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).When(x => x.MaxPrice.HasValue);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue);
        RuleFor(x => x.CategoryIds!.Count).LessThanOrEqualTo(50).When(x => x.CategoryIds is not null);
        RuleForEach(x => x.CategoryIds).GreaterThan(0);
        RuleFor(x => x.SortBy).IsInEnum();
    }
}

public class GetRelatedProductsQueryValidator : AbstractValidator<GetRelatedProductsQuery>
{
    public GetRelatedProductsQueryValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Count).InclusiveBetween(1, 24);
    }
}
