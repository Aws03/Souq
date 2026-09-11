using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.Domain.Common;

namespace Souq.Infrastructure.Persistence.Interceptors;

// ============================================================================
// TenantWriteGuardInterceptor — حارس الكتابة (MultiTenancy.md §4): يعمل داخل كل SaveChanges بلا
// استثناء، بجانب AuditTimestampsInterceptor، فلا مسار حفظ ينسى تطبيقه.
//   إضافة ⇒ يختم TenantId من السياق (الكيانات لا تملك setter له، والمعالجات لا تعرفه).
//   إضافة بمستأجر مختلف، أو تعديل/حذف صف مستأجر آخر، أو تغيير TenantId ⇒ CrossTenantWriteException
//   قبل أي SQL، مع سجلّ أمني حرج: وصول صف غريب إلى هنا يعني ثغرة تجاوزت مرشّح القراءة.
//   أي كتابة لبيانات متجر بلا سياق متجر (مضيف المنصّة، مهمة خلفية) ⇒ TenantContextMissingException.
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

    private void Reject(EntityEntry entry, int entityTenantId, int? contextTenantId)
    {
        var entityType = entry.Metadata.ClrType.Name;
        _logger.LogCritical(
            "Blocked cross-tenant write on {EntityType}: row tenant {EntityTenantId}, context tenant {ContextTenantId}",
            entityType, entityTenantId, contextTenantId);
        throw new CrossTenantWriteException(entityType, entityTenantId, contextTenantId);
    }
}
