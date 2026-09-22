using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Billing;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// قراءاتُ فوترة التاجر (C5، [ADR-0056](0056)). انضباطُ `BillingQueries` نفسه، ولسببه نفسه:
// جداولُ الشكل B بلا مرشّحٍ وبلا حارس كتابة، فكلُّ قراءةٍ تخصّ متجراً تحمل شرط `TenantId ==`
// صريحاً. وهذا الصنف مدرَجٌ في `ReviewedPlatformKeyedReads` بـ `TenancyRuleTests`.
//
// ============================================================================
// **والمبالغُ تُحتسب في الذاكرة من التجميعة نفسها، لا في SQL — وهذا قرارٌ لا كسل.**
//
// مجموعُ السطر تقريبٌ إلى خانات العملة الصغرى عبر `Money.FromCalculation` (ADR-0014)، والدينارُ
// ثلاثُ خانات. ولو كُتب هذا الحسابُ ثانيةً في SQL لصار للفاتورة **حسابان**: واحدٌ يراه من يفتحها
// وآخرُ يراه من يقرأ قائمتها — ويفترقان بفلسٍ في أوّل كمّيةٍ كسرية. فالقراءةُ تُحمّل الأسطر
// والمسدَّدات وتسأل التجميعةَ عن مجاميعها، فتكون الأرقام **من مصدرٍ واحد** أينما ظهرت.
//
// وثمنُه مقبولٌ ومحسوب: الترشيحُ والترتيبُ والترقيم كلُّها أعمدةُ الفاتورة نفسها فتقع في SQL،
// والتحميلُ يقع على صفحةٍ واحدة بعد الترقيم — عشرون فاتورةً لا الجدول كلّه.
//
// **و«متأخّرة» شرطٌ في SQL بلا حساب**: الحالةُ تصير `Settled` لحظةَ بلوغ المتبقّي صفراً، فـ
// `Issued` تعني بالضرورة أنّ عليها شيئاً — ومن ثمّ «صادرة ومرّ استحقاقُها» تكفي وحدها.
// ============================================================================
internal sealed class PlatformBillingQueries : IPlatformBillingQueries
{
    private readonly AppDbContext _db;

    public PlatformBillingQueries(AppDbContext db) => _db = db;

    public async Task<PaginatedList<PlatformInvoiceSummaryDto>> ListInvoicesAsync(
        PlatformInvoiceListFilter filter, PageRequest page, DateTime utcNow, CancellationToken ct)
    {
        var query = Filtered(filter, utcNow);

        var total = await query.CountAsync(ct);
        if (total == 0)
            return new PaginatedList<PlatformInvoiceSummaryDto>([], 0, page.Page, page.PageSize);

        // الأحدثُ أولاً. والمسوّداتُ بلا `IssuedAtUtc` فتتصدّر — وهو الصحيح لمن يحرّرها.
        var invoices = await query
            .Include(i => i.Lines).Include(i => i.Payments)
            .AsNoTracking()
            .OrderByDescending(i => i.IssuedAtUtc ?? DateTime.MaxValue).ThenByDescending(i => i.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(ct);

        // أسماءُ المتاجر بنداءٍ واحد لا نداءٍ لكل صفّ (N+1). و`Tenants` جدولٌ عالميّ بلا متجر.
        var tenantIds = invoices.Select(i => i.TenantId).Distinct().ToList();
        var names = await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var items = invoices
            .Select(i => Summary(i, names.GetValueOrDefault(i.TenantId), utcNow))
            .ToList();

        return new PaginatedList<PlatformInvoiceSummaryDto>(items, total, page.Page, page.PageSize);
    }

    public async Task<PlatformInvoiceDetailDto?> GetInvoiceAsync(
        int invoiceId, int? tenantId, DateTime utcNow, CancellationToken ct)
    {
        var query = _db.PlatformInvoices.AsNoTracking().Where(i => i.Id == invoiceId);

        // شرطُ المتجر حين يُقرأ عن متجر — وهو ما يجعل تاجراً لا يقرأ فاتورةَ غيره بتخمين رقم.
        // و`null` تعني المنصّةَ تقرأ، وهي وحدها من يصل إلى هذا المسار بلا متجر.
        if (tenantId is int tenant) query = query.Where(i => i.TenantId == tenant);

        var invoice = await query
            .Include(i => i.Lines).Include(i => i.Payments)
            .FirstOrDefaultAsync(ct);
        if (invoice is null) return null;

        var tenantName = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == invoice.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);

        var planName = invoice.PlanId is int planId
            ? await _db.Plans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.Name).FirstOrDefaultAsync(ct)
            : null;

        var notes = await _db.CreditNotes.AsNoTracking()
            .Include(n => n.Lines)
            .Where(n => n.PlatformInvoiceId == invoice.Id && n.Status == CreditNoteStatus.Issued)
            .OrderBy(n => n.Id)
            .ToListAsync(ct);

        // بريدُ مَن سجّل كلَّ سداد: نداءٌ واحد للفاتورة كلّها لا نداءٌ لكل سداد.
        //
        // وبلا `IgnoreQueryFilters` عمداً: مَن يسجّل سداداً حسابُ منصّة، والمرشّحُ في نطاق المنصّة
        // يصل إليه. وتاجرٌ يقرأ فاتورتَه لا يصل إلى حسابات المنصّة — فيرى معرّفاً بلا بريد، وهو
        // الصحيح: هويّةُ موظّفي المنصّة ليست من شأنه.
        var recorderIds = invoice.Payments.Select(p => p.RecordedByUserId).Distinct().ToList();
        var recorders = await _db.Users.AsNoTracking()
            .Where(u => recorderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return new PlatformInvoiceDetailDto(
            invoice.Id, invoice.TenantId, tenantName, invoice.Number, invoice.Status.ToString(), invoice.Currency,
            invoice.PlanId, planName, invoice.BillingPeriodId,
            invoice.PeriodStartUtc, invoice.PeriodEndUtc, invoice.IssuedAtUtc, invoice.DueAtUtc,
            invoice.IssuerName, invoice.IssuerAddress, invoice.IssuerTaxNumber,
            invoice.BilledToName, invoice.BilledToTaxNumber,
            invoice.PaymentInstructions, invoice.Notes,
            invoice.Subtotal.Amount, invoice.TaxAmount, invoice.Total.Amount,
            invoice.AmountPaid.Amount, invoice.CreditedAmount, invoice.Outstanding.Amount,
            invoice.IsOverdueAt(utcNow), invoice.DaysOverdueAt(utcNow),
            invoice.Lines.OrderBy(l => l.Id).Select(PlatformBillingMapper.ToDto).ToList(),
            invoice.Payments.OrderBy(p => p.ReceivedAtUtc).ThenBy(p => p.Id)
                .Select(p => new PlatformInvoicePaymentDto(
                    p.Id, p.Amount, p.Currency, p.Method.ToString(), p.ReceivedAtUtc,
                    p.RecordedByUserId, recorders.GetValueOrDefault(p.RecordedByUserId), p.Reference, p.Note))
                .ToList(),
            notes.Select(PlatformBillingMapper.ToDto).ToList(),
            PlatformBillingMapper.ToDto(invoice.TaxSnapshot));
    }

    public async Task<IReadOnlyList<BillingPeriodDto>> ListPeriodsAsync(int tenantId, CancellationToken ct) =>
        await _db.BillingPeriods.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.StartsAtUtc)
            .Select(p => new BillingPeriodDto(
                p.Id, p.TenantId, p.StartsAtUtc, p.EndsAtUtc, p.Status.ToString(), p.ClosedAtUtc,
                // عدّان مجمَّعان لا صفوف: «كم فيها» و«كم منها لم يُفوتر» هما ما يُقرأ في القائمة.
                _db.BillableEvents.Count(e => e.BillingPeriodId == p.Id && e.TenantId == tenantId),
                _db.BillableEvents.Count(e => e.BillingPeriodId == p.Id && e.TenantId == tenantId
                                              && e.PlatformInvoiceId == null)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BillableEventDto>> ListEventsAsync(
        int tenantId, int billingPeriodId, CancellationToken ct) =>
        await _db.BillableEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.BillingPeriodId == billingPeriodId)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Select(e => new BillableEventDto(
                e.Id, e.TenantId, e.Meter, e.Quantity, e.OccurredAtUtc,
                e.IdempotencyKey, e.Description, e.BillingPeriodId, e.PlatformInvoiceId))
            .ToListAsync(ct);

    // ============================================================================
    // ملخّصُ اشتراك متجرٍ واحد: خطتُه وسعرُها وما عليه.
    //
    // **والمستحقُّ يُجمع من الفواتير المفتوحة نفسها لا من عمودٍ مخزَّن**، فلا يوجد رقمٌ ثانٍ
    // يمكن أن يفترق. وهي فواتيرُ متجرٍ واحد، فالتحميلُ محدودٌ بعددها المفتوح لا بتاريخه كلّه.
    // ============================================================================
    public async Task<MySubscriptionDto> GetSubscriptionSummaryAsync(
        int tenantId, DateTime utcNow, CancellationToken ct)
    {
        var subscription = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new { s.PlanId, s.Status, s.StartedAtUtc })
            .FirstOrDefaultAsync(ct);

        var plan = subscription is null ? null : await _db.Plans.AsNoTracking()
            .Where(p => p.Id == subscription.PlanId)
            .Select(p => new { p.Code, p.Name, p.Version, p.Price, p.BillingIntervalMonths })
            .FirstOrDefaultAsync(ct);

        var open = await _db.PlatformInvoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == PlatformInvoiceStatus.Issued)
            .Include(i => i.Lines).Include(i => i.Payments)
            .ToListAsync(ct);

        var outstanding = open.Aggregate(0m, (sum, i) => sum + i.Outstanding.Amount);
        var overdue = open.Count(i => i.IsOverdueAt(utcNow));

        // تعليماتُ الدفع من **إعداد المنصّة الحالي** لا من فاتورةٍ بعينها: التاجر يقرأ هنا «كيف
        // أدفع الآن»، وحسابٌ بنكيّ تغيّر يجب أن يظهر فوراً. والمجمَّدُ على كل فاتورةٍ يبقى كما
        // كان يوم صدرت — والسؤالان مختلفان.
        var settings = await _db.PlatformBillingSettings.AsNoTracking().FirstOrDefaultAsync(ct);

        return new MySubscriptionDto(
            plan?.Code, plan?.Name, plan?.Version, subscription?.Status.ToString(), subscription?.StartedAtUtc,
            plan?.Price?.Amount, plan?.Price?.Currency, plan?.BillingIntervalMonths ?? 1,
            outstanding, open.FirstOrDefault()?.Currency ?? settings?.Currency,
            open.Count, overdue, settings?.PaymentInstructions);
    }

    // عدٌّ عابرٌ للمتاجر بلا إعادةِ صفٍّ لأحدها — وهو الشكلُ الذي يسمح به عُرفُ `PlatformQueries`.
    public Task<bool> AnyIssuedInvoiceAsync(CancellationToken ct) =>
        _db.PlatformInvoices.AsNoTracking().AnyAsync(i => i.Number != null, ct);

    private IQueryable<PlatformInvoice> Filtered(PlatformInvoiceListFilter filter, DateTime utcNow)
    {
        var query = _db.PlatformInvoices.AsQueryable();

        if (filter.TenantId is int tenantId) query = query.Where(i => i.TenantId == tenantId);
        if (filter.Status is PlatformInvoiceStatus status) query = query.Where(i => i.Status == status);

        // ما صدر وحده: مسوّدةٌ ليست مطالبةً بعد، ومسوّدةٌ أُلغيت لم تكن مطالبةً قطّ.
        if (filter.IssuedOnly)
            query = query.Where(i => i.Status == PlatformInvoiceStatus.Issued
                                     || i.Status == PlatformInvoiceStatus.Settled);

        // `Issued` تعني «عليها شيء» بالبناء (الحالةُ تصير `Settled` عند بلوغ الصفر)، فالتأخّرُ
        // شرطان على عمودين ولا يحتاج حساباً.
        if (filter.OverdueOnly)
            query = query.Where(i => i.Status == PlatformInvoiceStatus.Issued
                                     && i.DueAtUtc != null && i.DueAtUtc < utcNow);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            // رقمُ الفاتورة أو اسمُ المُرسَل إليه المجمَّد عليها — لا وصلةٌ إلى اسمِ متجرٍ قد
            // يكون تغيّر: الفاتورة تُبحَث بما تقوله هي.
            query = query.Where(i => (i.Number != null && i.Number.Contains(search))
                                     || (i.BilledToName != null && i.BilledToName.Contains(search)));
        }

        return query;
    }

    private static PlatformInvoiceSummaryDto Summary(PlatformInvoice invoice, string? tenantName, DateTime utcNow) =>
        new(invoice.Id, invoice.TenantId, tenantName, invoice.Number, invoice.Status.ToString(), invoice.Currency,
            invoice.PeriodStartUtc, invoice.PeriodEndUtc, invoice.IssuedAtUtc, invoice.DueAtUtc,
            invoice.Subtotal.Amount, invoice.TaxAmount, invoice.Total.Amount,
            invoice.AmountPaid.Amount, invoice.CreditedAmount, invoice.Outstanding.Amount,
            invoice.IsOverdueAt(utcNow), invoice.DaysOverdueAt(utcNow));
}
