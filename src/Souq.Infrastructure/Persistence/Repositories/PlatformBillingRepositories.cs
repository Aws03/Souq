using Microsoft.EntityFrameworkCore;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Repositories;

// ============================================================================
// منافذُ كتابة فوترة التاجر (C5، [ADR-0056](0056)).
//
// **القاعدةُ التي يقوم عليها عزلُ كل ما هنا، مكتوبةً حيث تُقرأ:** الفواتيرُ وإشعاراتُ الدائن
// وفتراتُ الفوترة وأحداثُها جداولُ منصّةٍ بمفتاح متجر (الشكل B في ADR-0047 §1) — بلا مرشّحٍ
// وبلا حارس كتابة. فكلُّ قراءةٍ تخصّ متجراً **تحمل شرط `TenantId` صريحاً**، ولا واحدةَ منها
// تُستدعى إلّا من حالة استخدامٍ مُدقَّقة. وهذا الملفّ مُدرَجٌ عمداً في `ReviewedPlatformKeyedReads`
// بـ `TenancyRuleTests`: لا يقرأ هذه الجداولَ أحدٌ بلا مراجعة.
//
// والقراءةُ بمعرّفٍ **وحده** موجودةٌ عمداً في مسارَين فقط — إصدارُ المستند وقيدُه — وكلاهما يقع
// في نطاق المنصّة عن متجرٍ تختاره المنصّة. وما يقرؤه **تاجرٌ** عن نفسه يمرّ بـ `GetForTenantAsync`
// حصراً، فمعرّفٌ مخمَّن لفاتورةِ غيره يُجيب «غير موجودة» لا «ممنوع» (عُرفُ المستودع: 404 لا 403).
// ============================================================================

public class PlatformBillingSettingsRepository : IPlatformBillingSettingsRepository
{
    private readonly AppDbContext _db;

    public PlatformBillingSettingsRepository(AppDbContext db) => _db = db;

    // صفٌّ واحد عالميّ (الشكل C): لا شرطَ متجرٍ له ولا مرشّحَ عليه.
    public Task<PlatformBillingSettings?> GetAsync(CancellationToken ct = default) =>
        _db.PlatformBillingSettings.FirstOrDefaultAsync(ct);

    public void Add(PlatformBillingSettings settings) => _db.PlatformBillingSettings.Add(settings);
}

public class PlatformInvoiceRepository : RepositoryBase<PlatformInvoice>, IPlatformInvoiceRepository
{
    public PlatformInvoiceRepository(AppDbContext db) : base(db) { }

    public Task<PlatformInvoice?> GetWithDetailsAsync(int id, CancellationToken ct = default) =>
        Db.PlatformInvoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    // شرطُ المتجر صريحٌ وإلى جانب المعرّف: هذا هو ما يمنع تاجراً من قراءة فاتورةِ تاجرٍ آخر.
    public Task<PlatformInvoice?> GetForTenantAsync(int id, int tenantId, CancellationToken ct = default) =>
        Db.PlatformInvoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId, ct);

    // ========================================================================
    // **القراءةُ الوحيدة هنا التي لا تحمل شرطَ متجر، وهي مقصودة**: المطالبة عملُ منصّةٍ يمرّ على
    // ما استحقّ عبر المتاجر كلّها — وهو بالضبط الشكلُ الذي يسمح به عُرفُ `PlatformQueries`
    // لقراءةٍ عابرة، ما دامت تُستدعى من مسارٍ واحدٍ مُدقَّق ومحروس بعقد إيجار.
    //
    // و`Issued` وحدها: `Settled` لم يبقَ عليها شيء، و`Draft` لم تُطالِب بشيء، و`Cancelled` لم
    // تكن مطالبةً قطّ. والأقدمُ أوّلاً كي لا يتأخّر سلّمُ فاتورةٍ قديمة خلف أحدث منها.
    // ========================================================================
    public async Task<IReadOnlyList<PlatformInvoice>> ListOverdueAsync(
        DateTime utcNow, int max, CancellationToken ct = default) =>
        await Db.PlatformInvoices
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .Where(i => i.Status == PlatformInvoiceStatus.Issued && i.DueAtUtc != null && i.DueAtUtc < utcNow)
            .OrderBy(i => i.DueAtUtc).ThenBy(i => i.Id)
            .Take(max)
            .ToListAsync(ct);
}

public class CreditNoteRepository : RepositoryBase<CreditNote>, ICreditNoteRepository
{
    public CreditNoteRepository(AppDbContext db) : base(db) { }

    public Task<CreditNote?> GetWithLinesAsync(int id, CancellationToken ct = default) =>
        Db.CreditNotes.Include(n => n.Lines).FirstOrDefaultAsync(n => n.Id == id, ct);

    // بمعرّف الفاتورة لا بمعرّف متجر: الفاتورةُ نفسها هي من قُرئت بشرط متجرها قبل هذا النداء،
    // وإشعاراتُها تابعةٌ لها لا لمتجرٍ يُسأل عنه ثانيةً.
    public async Task<IReadOnlyList<CreditNote>> ListForInvoiceAsync(int platformInvoiceId, CancellationToken ct = default) =>
        await Db.CreditNotes.Include(n => n.Lines)
            .Where(n => n.PlatformInvoiceId == platformInvoiceId)
            .OrderBy(n => n.Id)
            .ToListAsync(ct);
}

public class BillingPeriodRepository : RepositoryBase<BillingPeriod>, IBillingPeriodRepository
{
    public BillingPeriodRepository(AppDbContext db) : base(db) { }

    // بلا ترشيحٍ بالحالة: المُنادي يفرّق بين «لا فترة» و«فترةٌ أُغلقت»، وهما جوابان مختلفان.
    public Task<BillingPeriod?> FindCoveringAsync(int tenantId, DateTime instant, CancellationToken ct = default) =>
        Db.BillingPeriods.FirstOrDefaultAsync(
            p => p.TenantId == tenantId && p.StartsAtUtc <= instant && p.EndsAtUtc > instant, ct);

    public Task<BillingPeriod?> GetForTenantAsync(int id, int tenantId, CancellationToken ct = default) =>
        Db.BillingPeriods.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);
}

public class BillableEventRepository : RepositoryBase<BillableEvent>, IBillableEventRepository
{
    public BillableEventRepository(AppDbContext db) : base(db) { }

    public Task<BillableEvent?> FindByKeyAsync(int tenantId, string idempotencyKey, CancellationToken ct = default)
    {
        var key = idempotencyKey?.Trim() ?? "";
        return Db.BillableEvents.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.IdempotencyKey == key, ct);
    }

    // شرطُ المتجر مكتوبٌ وإن كان معرّفُ الفترة يكفي منطقياً: الفترةُ تخصّ متجراً واحداً فعلاً،
    // لكنّ الاعتمادَ على ذلك يجعل عزلَ هذه القراءة يرثه صفٌّ آخر بدل أن تحمله هي.
    public async Task<IReadOnlyList<BillableEvent>> ListUnbilledAsync(
        int tenantId, int billingPeriodId, CancellationToken ct = default) =>
        await Db.BillableEvents
            .Where(e => e.TenantId == tenantId && e.BillingPeriodId == billingPeriodId && e.PlatformInvoiceId == null)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .ToListAsync(ct);
}
