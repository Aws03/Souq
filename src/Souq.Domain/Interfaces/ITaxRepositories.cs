using Souq.Domain.Entities;
using Souq.Domain.Platform;

namespace Souq.Domain.Interfaces;

// مستودعاتُ الضريبة (ADR-0055). الملفّ جدولُ منصّةٍ بلا متجر؛ وإعدادُ المتجر ملكٌ لمتجرٍ يعيش
// داخل مرشّحه.

public interface ITaxProfileRepository : IRepository<TaxProfile>
{
    // الملفّ بإصداراته ونسبِها: قراراتُ التجمّع كلّها تحتاج الشجرة لا الجذر وحده (النشر يفحص
    // النسب، والمسوّدة تُقاس بآخر منشور).
    Task<TaxProfile?> GetWithVersionsAsync(int id, CancellationToken ct = default);

    Task<TaxProfile?> FindByJurisdictionAsync(string jurisdiction, CancellationToken ct = default);

    Task<IReadOnlyList<TaxProfile>> ListWithVersionsAsync(CancellationToken ct = default);
}

public interface IStoreTaxSettingsRepository
{
    // إعدادُ متجر السياق. null ⇒ لم يُضبَط بعد ⇒ لا ملفّ ولا جمع (وهو حال كل متجر قائم).
    Task<StoreTaxSettings?> GetAsync(CancellationToken ct = default);

    void Add(StoreTaxSettings settings);
}
