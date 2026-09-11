using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Auditing;
using Souq.Domain.Auditing;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Auditing;

// ============================================================================
// AuditTrail — IAuditTrail فوق وحدة العمل الحالية (AppDbContext النطاق): السطر يُضاف للمتتبِّع قبل المعالج فيحفظه
// أول SaveChanges له في المعاملة نفسها. طلبات متداخلة ممكنة (مكدّس). بعد النجاح: سطر لم يُحفظ بعد (استعلام،
// أمر بلا أثر) يُحفظ هنا؛ بعد الفشل: يُفصل عن المتتبِّع إن لم يُحفظ.
// ============================================================================
internal sealed class AuditTrail : IAuditTrail
{
    private readonly AppDbContext _db;
    private readonly Stack<AuditEntry> _staged = new();

    public AuditTrail(AppDbContext db) => _db = db;

    public void Stage(AuditEntry entry)
    {
        _db.AuditEntries.Add(entry);
        _staged.Push(entry);
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        if (!_staged.TryPop(out var entry)) return;
        if (_db.Entry(entry).State == EntityState.Added)
            await _db.SaveChangesAsync(ct);
    }

    public void Discard()
    {
        if (!_staged.TryPop(out var entry)) return;
        var tracked = _db.Entry(entry);
        if (tracked.State == EntityState.Added)
            tracked.State = EntityState.Detached;
    }
}
