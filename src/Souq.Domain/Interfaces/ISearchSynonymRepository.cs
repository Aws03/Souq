using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// ============================================================================
// مفردات بحث المتجر (M3، ADR-0042). جدول صغير يملكه التاجر، فالكتابة تمرّ بمستودع كبقية الكتابة — والقراءة
// للعرض تمرّ بمنفذ القراءة (ICatalogQueries) كعادة هذا المستودع. Exists هنا لرسالة خطأ واضحة قبل أن يرفض
// الفهرس الفريد الصفّ المكرّر: الفهرس هو الحارس الحقيقي، وهذه لطف بالتاجر.
// ============================================================================
public interface ISearchSynonymRepository : IRepository<SearchSynonym>
{
    Task<bool> ExistsAsync(string culture, string termNormalized, string expansionNormalized,
        int? excludingId = null, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);
}
