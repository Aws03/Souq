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

    // ============================================================================
    // معرّفُ تنفيذ هذا البحث (C9، ADR-0050 §3) — يُصكّ لحظةَ الاستعلام ويُعاد هنا كي يردّه العميل
    // مع النقرة والإضافة والشراء. **وبلا ردٍّ لا تُنسَب مبيعةٌ إلى البحث الذي أنتجها أبداً**: القُرب
    // الزمنيّ ليس نسبةً، ولا تُستخرَج النسبة لاحقاً من صفوفٍ لا تحملها. والحقلُ الثاني يُضاف بالحيلة
    // نفسها التي أُضيف بها `search`: وراثةُ شكل الترقيم تُبقي كلَّ عميلٍ قائم يعمل.
    //
    // null ⇒ الالتقاط معطّل: لا معرّف بلا حدثٍ يحمله.
    // ============================================================================
    public Guid? SearchExecutionId { get; }

    public ProductSearchPage(
        PaginatedList<ProductDto> page, SearchRecovery? search = null, Guid? searchExecutionId = null)
        : base(page.Items, page.TotalCount, page.PageNumber, page.PageSize)
    {
        Search = search;
        SearchExecutionId = searchExecutionId;
    }

    // نسخةٌ بمعرّف التنفيذ: منفذ القراءة (`ICatalogQueries`) لا يعرف الالتقاط، والمعالجُ هو من
    // يصكّ — فالإضافة تقع بعد القراءة لا داخلها.
    public ProductSearchPage WithSearchExecution(Guid id) => new(this, Search, id);
}

// ============================================================================
// ما يُقال للمتسوّق عن بحثه حين لا تُطابِق كلماته شيئاً:
//   • SearchedInstead — البحث جرى بكلمة أخرى، **وهذه النتائج لها**. تُعرض صراحةً ("نتائج …" مع رابط للإصرار على
//     الأصل) ولا تُستبدَل كلماته صامتةً — المبدأ نفسه الذي يمنع اختيار متغيّر ضمناً في V3 (ADR-0041).
//   • Category — لا نتائج بحال، لكن اسم فئة يطابق: بابٌ بدل نهاية مسدودة.
// ============================================================================
public sealed record SearchRecovery(string Term, string? SearchedInstead, SearchCategorySuggestion? Category);

public sealed record SearchCategorySuggestion(int Id, string Slug, string Name);
