using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Infrastructure.Persistence.Repositories;

// مفردات بحث المتجر (M3). بلا أي شرط على المستأجر في أي استعلام: المرشّح العام يضيفه — وإضافته بيد هنا
// كانت ستُخفي أنّ المرشّح هو الحارس.
public class SearchSynonymRepository : RepositoryBase<SearchSynonym>, ISearchSynonymRepository
{
    public SearchSynonymRepository(AppDbContext db) : base(db) { }

    public Task<bool> ExistsAsync(string culture, string termNormalized, string expansionNormalized,
        int? excludingId = null, CancellationToken ct = default) =>
        Db.SearchSynonyms.AnyAsync(s => s.Culture == culture
                                        && s.TermNormalized == termNormalized
                                        && s.ExpansionNormalized == expansionNormalized
                                        && (excludingId == null || s.Id != excludingId), ct);

    public Task<int> CountAsync(CancellationToken ct = default) => Db.SearchSynonyms.CountAsync(ct);
}
