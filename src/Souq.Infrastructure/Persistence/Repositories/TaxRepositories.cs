using Microsoft.EntityFrameworkCore;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Repositories;

// ============================================================================
// مستودعا الضريبة (ADR-0055).
//
// `TaxProfiles` جدولُ منصّةٍ **بلا `TenantId`** — كـ `Plans` — فلا مرشّحَ مستأجرٍ عليه ولا شرطَ
// متجرٍ يُكتب بيد: هو قائمةٌ عامّة يقرؤها الجميع ولا تخصّ أحداً. وإعدادُ المتجر `ITenantOwned`،
// فمرشّحُه يُطبَّق ولا يُكتب شرطُه أيضاً: الأولُ لا يحتاجه، والثاني يأتيه من المرشّح.
// ============================================================================
public class TaxProfileRepository : RepositoryBase<TaxProfile>, ITaxProfileRepository
{
    public TaxProfileRepository(AppDbContext db) : base(db) { }

    public Task<TaxProfile?> GetWithVersionsAsync(int id, CancellationToken ct = default) =>
        Db.TaxProfiles
            .Include(p => p.Versions).ThenInclude(v => v.Rates)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<TaxProfile?> FindByJurisdictionAsync(string jurisdiction, CancellationToken ct = default)
    {
        var normalized = jurisdiction?.Trim().ToUpperInvariant() ?? "";
        return Db.TaxProfiles
            .Include(p => p.Versions).ThenInclude(v => v.Rates)
            .FirstOrDefaultAsync(p => p.Jurisdiction == normalized, ct);
    }

    public async Task<IReadOnlyList<TaxProfile>> ListWithVersionsAsync(CancellationToken ct = default) =>
        await Db.TaxProfiles
            .Include(p => p.Versions).ThenInclude(v => v.Rates)
            .OrderBy(p => p.Jurisdiction)
            .ToListAsync(ct);
}

public class StoreTaxSettingsRepository : IStoreTaxSettingsRepository
{
    private readonly AppDbContext _db;

    public StoreTaxSettingsRepository(AppDbContext db) => _db = db;

    // صفٌّ واحد لكل متجر، والمرشّح يحصره في متجر السياق — فلا شرطَ متجرٍ هنا ولا حاجةَ إليه.
    public Task<StoreTaxSettings?> GetAsync(CancellationToken ct = default) =>
        _db.StoreTaxSettings.FirstOrDefaultAsync(ct);

    public void Add(StoreTaxSettings settings) => _db.StoreTaxSettings.Add(settings);
}
