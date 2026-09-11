using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;

namespace Souq.Infrastructure.Persistence.Interceptors;

// ============================================================================
// TenantWriteGuardInterceptor — حارس الكتابة (MultiTenancy.md §4، ADR-0022): يعمل داخل كل SaveChanges
// بلا استثناء، بجانب AuditTimestampsInterceptor، فلا مسار حفظ ينسى تطبيقه.
//   صف متجر (ITenantOwned):
//     إضافة ⇒ يختم TenantId من السياق (الكيانات لا تملك setter له، والمعالجات لا تعرفه).
//     إضافة بمتجر مختلف، أو تعديل/حذف صف متجر آخر، أو تغيير TenantId ⇒ CrossTenantWriteException.
//     أي كتابة بلا سياق متجر (مضيف المنصّة، مهمة خلفية) ⇒ TenantContextMissingException.
//   حساب أو جلسة (ITenantOrPlatformOwned):
//     حساب منصّة يُكتب في نطاق المنصّة فقط (بلا متجر)، وحساب متجر في نطاق متجره فقط (يُختم).
// الرفض قبل أي SQL، مع سجلّ أمني حرج: وصول صف غريب إلى هنا يعني ثغرة تجاوزت مرشّح القراءة.
// ============================================================================
public sealed class TenantWriteGuardInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<TenantWriteGuardInterceptor> _logger;

    public TenantWriteGuardInterceptor(ILogger<TenantWriteGuardInterceptor> logger) => _logger = logger;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Guard(DbContext? context)
    {
        if (context is not AppDbContext db) return;
        GuardTenantOwned(db);
        GuardTenantOrPlatformOwned(db);
    }

    private void GuardTenantOwned(AppDbContext db)
    {
        var current = db.Tenancy.Tenant?.Id;
        foreach (var entry in db.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (current is null) throw new TenantContextMissingException();

            var tenantId = entry.Property(nameof(ITenantOwned.TenantId));
            if (entry.State == EntityState.Added)
            {
                // TenantId جزء من المفتاح البديل (TenantId, Id) للمراجع داخل المتجر؛ قيمة مؤقتة من EF
                // (لا من كود) تعني "لم يُعيَّن بعد" تماماً مثل الصفر.
                var assigned = (int)tenantId.CurrentValue!;
                if (assigned == 0 || tenantId.IsTemporary)
                {
                    tenantId.CurrentValue = current.Value;
                    tenantId.IsTemporary = false;
                }
                else if (assigned != current.Value) Reject(entry, assigned, current);
            }
            else
            {
                // Modified/Deleted: المالك هو القيمة كما قُرئت من القاعدة، ولا يُسمح بتغييرها.
                var owner = (int)tenantId.OriginalValue!;
                if (owner != current.Value || (int)tenantId.CurrentValue! != owner) Reject(entry, owner, current);
            }
        }
    }

    private void GuardTenantOrPlatformOwned(AppDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<ITenantOrPlatformOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;

            var scope = db.Tenancy.Scope;
            if (scope == TenantScope.None) throw new TenantContextMissingException();
            var expected = scope == TenantScope.Tenant ? db.Tenancy.Tenant!.Id : (int?)null;

            var tenantId = entry.Property(nameof(ITenantOrPlatformOwned.TenantId));
            if (entry.State == EntityState.Added)
            {
                // حساب منصّة داخل متجر، أو حساب متجر في نطاق المنصّة ⇒ خطأ برمجي لا يُحفَظ أبداً.
                if (entry.Entity.BelongsToPlatform != (scope == TenantScope.Platform))
                    Reject(entry, (int?)tenantId.CurrentValue, expected);

                var assigned = (int?)tenantId.CurrentValue;
                if (assigned is null && expected is not null) tenantId.CurrentValue = expected;
                else if (assigned != expected) Reject(entry, assigned, expected);
            }
            else
            {
                var owner = (int?)tenantId.OriginalValue;
                if (owner != expected || (int?)tenantId.CurrentValue != owner) Reject(entry, owner, expected);
            }
        }
    }

    private void Reject(EntityEntry entry, int? entityTenantId, int? contextTenantId)
    {
        var entityType = entry.Metadata.ClrType.Name;
        _logger.LogCritical(
            "Blocked cross-tenant write on {EntityType}: row tenant {EntityTenantId}, context tenant {ContextTenantId}",
            entityType, entityTenantId, contextTenantId);
        throw new CrossTenantWriteException(entityType, entityTenantId, contextTenantId);
    }
}
