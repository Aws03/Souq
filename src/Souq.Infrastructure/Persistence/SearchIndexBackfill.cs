using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// تعبئة الصورة المطبَّعة للبحث لصفوف سابقة للحقل (M3، ADR-0042).
//
// لماذا هنا وليس في الهجرة: التطبيع دالّة Unicode في المجال (SearchText.Normalize) — تفكيك FormD وطيّ صور الألف
// والتاء المربوطة. كتابتها ثانيةً بـ T-SQL داخل الهجرة يعني تنفيذين لقاعدة واحدة، وأي تباعد بينهما يجعل النص
// المفهرس لا يطابق نصّ الاستعلام — وهو عطل صامت لا يُكتشف إلا بشكوى متسوّق. فالتعبئة تمرّ بالتنفيذ نفسه.
//
// لماذا عند كل إقلاع: الفحص عملياً بلا تكلفة. الشرط `NameNormalized == ""` قيمة محدَّدة في الفهرس
// IX_*_TenantId_NameNormalized، فهو بحث فهرس (seek) يعود بصفر صفّاً بعد أول تعبئة — لا مسح جدول. وبه تكون
// العملية **مُصلِحة لنفسها**: أي صفّ يخرج بصورة مطبَّعة فارغة لأي سبب يُصلَح في الإقلاع التالي.
//
// كل المتاجر لا النشطة وحدها: متجر موقوف يُستأنف لاحقاً بلا أي تعديل على كتالوجه كان سيبقى بفهرس بحث فارغ.
// ============================================================================
public static class SearchIndexBackfill
{
    // حجم الدفعة: صفوف الترجمة تُقرأ وتُحفظ على دفعات كي لا يحمل متجر بكتالوج كبير كلّ صفوفه في الذاكرة مرّة واحدة.
    private const int BatchSize = 500;

    public static async Task RunAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        var tenants = await AllTenantsAsync(services, ct);
        var directory = services.GetRequiredService<ITenantDirectory>();
        var repaired = 0;

        foreach (var tenantId in tenants)
        {
            var tenant = await directory.FindByIdAsync(tenantId, ct);
            if (tenant is null) continue;

            repaired += await TenantScopes.RunAsync(services, tenant, async tenantServices =>
            {
                var db = tenantServices.GetRequiredService<AppDbContext>();
                return await RebuildAsync<ProductTranslation>(db, ct) + await RebuildAsync<CategoryTranslation>(db, ct);
            });
        }

        if (repaired > 0)
            logger.LogInformation("Search index backfilled: {RepairedRows} catalog translation rows normalized", repaired);
    }

    // المتاجر كلها من نطاق المنصّة: جدول Tenants ليس ملكاً لمتجر، ومرشّح المستأجر يسمح بجداول المنصّة في نطاقها.
    private static async Task<List<int>> AllTenantsAsync(IServiceProvider services, CancellationToken ct)
    {
        List<int> ids = [];
        await TenantScopes.RunPlatformAsync(services, async platformServices =>
            ids = await platformServices.GetRequiredService<AppDbContext>().Tenants
                .AsNoTracking().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(ct));
        return ids;
    }

    // ============================================================================
    // الصفوف التي لا صورة مطبَّعة لها بعد. تُقرأ متتبَّعة (لا AsNoTracking) لأنّ RebuildSearchText يكتب فيها،
    // ثم SaveChanges يمرّ بحارس الكتابة وختم الطوابع كأي كتابة أخرى — لا كتابة مجمَّعة تتجاوزهما.
    //
    // يُعاد القراءة بعد كل دفعة بشرط أنّ الصفّ ما زال فارغ الصورة، فلا يعتمد الترقيم على إزاحة تتغيّر تحته.
    // وإن عادت دفعة بلا أي تغيير فعلي (اسم كلّه ترقيم مثلاً، صورته فارغة بحقّ) تُقطَع الحلقة كي لا تدور أبداً.
    // ============================================================================
    private static async Task<int> RebuildAsync<T>(AppDbContext db, CancellationToken ct) where T : CatalogTranslation
    {
        var repaired = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.Set<T>()
                .Where(t => t.NameNormalized == "")
                .OrderBy(t => t.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            var changed = batch.Count(row => row.RebuildSearchText());
            if (changed == 0)
            {
                // صورة فارغة صحيحة (لا حرف ولا رقم في الاسم): تُترك، ولا تُعَدّ إصلاحاً، ولا تُقرأ دفعة أخرى.
                db.ChangeTracker.Clear();
                break;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            repaired += changed;
        }

        return repaired;
    }
}
