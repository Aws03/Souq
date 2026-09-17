using FluentValidation;
using MediatR;
using Souq.Application.Common.Tenancy;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// اقتراحات البحث أثناء الكتابة (M3، ADR-0042).
//
// **تُقترح منتجات وفئات حقيقية، لا كلمات.** الفرق ليس تجميلاً: كلمة مقترحة تُلزم المتسوّق بضغطة ثانية ثم بقراءة
// صفحة نتائج، أمّا منتج مقترح فهو الوجهة نفسها — ويُطمئنه أنّ ما يبحث عنه موجود قبل أن يكمل كتابته. وهذا أيضاً
// ما يجعل الاقتراحات بحث فهرس (بادئة على الصورة المطبَّعة) لا حساباً على مفردات.
//
// الاقتراحات لا تُصحِّح خطأً مطبعياً: المتسوّق ما زال يكتب، فتصحيح كلمة ناقصة نصفها تخمينٌ يقفز على يده.
// الاسترجاع يعمل عند **تنفيذ** البحث (SearchProductsAsync)، حيث صار للكلمة صورة نهائية.
// ============================================================================
public sealed record GetSearchSuggestionsQuery(string? Keyword, int Limit = SearchSuggestionRules.DefaultLimit)
    : IRequest<IReadOnlyList<SearchSuggestionDto>>;

public static class SearchSuggestionRules
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 10;

    // أقصر من هذا يطابق نصف الكتالوج: اقتراح بلا معلومة، وطلب على كل حرف.
    public const int MinKeywordLength = 2;

    // نفس حدّ كلمة البحث في بقية الكتالوج — لا سبب لقبول أطول من ذلك هنا.
    public const int MaxKeywordLength = 200;
}

// Kind: "product" أو "category" — نصّ لا enum في العقد كي يضيف الخادم نوعاً ثالثاً بلا كسر عميل قائم.
public sealed record SearchSuggestionDto(string Kind, int Id, string Slug, string Name, string? ImageUrl);

public sealed class GetSearchSuggestionsValidator : AbstractValidator<GetSearchSuggestionsQuery>
{
    public GetSearchSuggestionsValidator()
    {
        RuleFor(x => x.Keyword).MaximumLength(SearchSuggestionRules.MaxKeywordLength);
        RuleFor(x => x.Limit).InclusiveBetween(1, SearchSuggestionRules.MaxLimit);
    }
}

public class GetSearchSuggestionsHandler : IRequestHandler<GetSearchSuggestionsQuery, IReadOnlyList<SearchSuggestionDto>>
{
    private readonly ICatalogQueries _catalog;
    private readonly ITenantContext _tenant;

    public GetSearchSuggestionsHandler(ICatalogQueries catalog, ITenantContext tenant)
    {
        _catalog = catalog; _tenant = tenant;
    }

    public Task<IReadOnlyList<SearchSuggestionDto>> Handle(GetSearchSuggestionsQuery q, CancellationToken ct) =>
        _catalog.SuggestAsync(q.Keyword, q.Limit, _tenant.RequireTenant().DefaultCulture, ct);
}
