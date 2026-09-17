using MediatR;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// مفردات المتجر لشاشة التاجر (M3، ADR-0042). الجدول صغير بحدّه (SearchSynonymRules.MaxPerStore) فلا ترقيم:
// قائمة كاملة أسهل على من يراجع مفرداته من صفحات يتنقّل بينها.
//
// يُعاد ما كتبه التاجر (Term/Expansion) **وصورته المطبَّعة معاً**: الصورة هي ما يُطابَق فعلاً، وإظهارها يجعل
// السلوك مفهوماً — التاجر يرى بنفسه أنّ "مكنسة" و"مكنسه" الشيء نفسه هنا، فلا يضيف الصفّ مرّتين ويستغرب الرفض.
// ============================================================================
public sealed record SearchSynonymDto(
    int Id, string Culture, string Term, string TermNormalized, string Expansion, string ExpansionNormalized);

public sealed record ListSearchSynonymsQuery : IRequest<IReadOnlyList<SearchSynonymDto>>;

public class ListSearchSynonymsHandler : IRequestHandler<ListSearchSynonymsQuery, IReadOnlyList<SearchSynonymDto>>
{
    private readonly ICatalogQueries _catalog;
    public ListSearchSynonymsHandler(ICatalogQueries catalog) => _catalog = catalog;

    public Task<IReadOnlyList<SearchSynonymDto>> Handle(ListSearchSynonymsQuery q, CancellationToken ct) =>
        _catalog.ListSearchSynonymsAsync(ct);
}
