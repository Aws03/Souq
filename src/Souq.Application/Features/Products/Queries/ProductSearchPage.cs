using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// صفحة نتائج البحث (M3، ADR-0042) — `PaginatedList<ProductDto>` نفسها، وقد ورثت منها عمداً: كل حقول الترقيم
// القائمة تبقى كما هي في JSON، ويُضاف حقل `search` وحده. عميل قائم لا يتأثّر، ولا يُكرَّر شكل الترقيم في نوع ثانٍ.
// ============================================================================
public sealed class ProductSearchPage : PaginatedList<ProductDto>
{
    // null ⇒ لا شيء يُقال للمتسوّق: إمّا وُجدت نتائج بكلماته هو، وإمّا لا استرجاع ممكناً.
    public SearchRecovery? Search { get; }

    public ProductSearchPage(PaginatedList<ProductDto> page, SearchRecovery? search = null)
        : base(page.Items, page.TotalCount, page.PageNumber, page.PageSize) => Search = search;
}

// ============================================================================
// ما يُقال للمتسوّق عن بحثه حين لا تُطابِق كلماته شيئاً:
//   • SearchedInstead — البحث جرى بكلمة أخرى، **وهذه النتائج لها**. تُعرض صراحةً ("نتائج …" مع رابط للإصرار على
//     الأصل) ولا تُستبدَل كلماته صامتةً — المبدأ نفسه الذي يمنع اختيار متغيّر ضمناً في V3 (ADR-0041).
//   • Category — لا نتائج بحال، لكن اسم فئة يطابق: بابٌ بدل نهاية مسدودة.
// ============================================================================
public sealed record SearchRecovery(string Term, string? SearchedInstead, SearchCategorySuggestion? Category);

public sealed record SearchCategorySuggestion(int Id, string Slug, string Name);
