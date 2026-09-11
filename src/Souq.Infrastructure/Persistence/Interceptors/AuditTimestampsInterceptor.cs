using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Souq.Domain.Common;

namespace Souq.Infrastructure.Persistence.Interceptors;

// ============================================================================
// AuditTimestampsInterceptor — يختم CreatedAt/UpdatedAt لكل كيان عند الحفظ من TimeProvider
// (لا DateTime.UtcNow: الوقت قابل للتثبيت في الاختبارات — Phase 0 D12). كان هذا داخل
// AppDbContext.SaveChangesAsync؛ صار معترِضاً مستقلاً لأن النمط سيتكرّر: المرحلة 2 تضيف
// بجانبه حارس المستأجر (ختم TenantId ورفض الكتابة عبر المستأجرين) دون تضخيم AppDbContext
// ودون أن ينسى أيّ مسار حفظ تطبيقه (MultiTenancy.md §4).
// ============================================================================
public sealed class AuditTimestampsInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _clock;
    public AuditTimestampsInterceptor(TimeProvider clock) => _clock = clock;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            else if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }
}
