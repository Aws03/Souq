using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Billing;

// ============================================================================
// القياسُ وفتراتُه (C5، [ADR-0056](0056)، CommercialPlatformArchitecture §4.5): دفترُ الوحدات
// القابلة للفوترة، وإغلاقُ الفترة، وتحميلُ ما قيس على فاتورة.
//
// **ولا شيءَ في المنتج يُصدر مقياساً بعد، وهذا مكتوبٌ كي لا يُفترض خلافُه.** ما يُبنى هنا هو
// الدفترُ وآليةُ الإغلاق — وهما ما تسمّيه المعماريّةُ «الآن» — أمّا الشرائحُ المسعَّرة بالاستهلاك
// فمؤجّلةٌ حتى يجري قياسٌ حقيقيّ لمدّة، وحتى يُجيب المالك `C-12`. والسابقةُ مقصودة: `TaxSnapshot`
// شُحن شكلاً بلا كاتبٍ للسبب نفسه، ثمّ وجد كاتبَه في الشريحة التالية.
//
// **والسعرُ لا يأتي من هنا أبداً.** تحميلُ وحداتٍ على فاتورة يأخذ سعرَ الوحدة **وسيطاً** من
// المشغّل: الآلةُ تعرف كم قيس، ولا تعرف بكم يُباع — وتخمينُها كان قراراً تجاريّاً بالصدفة.
// ============================================================================

// ============================================================================
// تسجيلُ وحدةٍ قابلة للفوترة. **غيرُ مكرِّر بمفتاحه**: إعادةُ محاولةٍ بعد انقطاعٍ شبكيّ تُعيد
// الحدثَ القائم ولا تُنشئ ثانياً، والفهرسُ الفريد هو الحاسم في القاعدة لا هذا الفحص.
//
// والحدثُ يجد فترتَه المفتوحة أو تُفتَح له: فترةٌ شهريّة بحدود يوم الحدث نفسه. وحدثٌ يقع في مدّةِ
// فترةٍ **أُغلقت** يُرفض — لا يلتحق بها ولا يُزحزَح صامتاً، لأنّ فاتورتَها قد صدرت.
// ============================================================================
public record RecordBillableEventCommand(
    int TenantId, string Meter, decimal Quantity, DateTime OccurredAtUtc,
    string IdempotencyKey, string? Description)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.event.recorded", "BillableEvent", IdempotencyKey, TenantId,
        InvoiceErrors.Meta(("meter", Meter), ("quantity", Quantity), ("occurredAt", OccurredAtUtc)));
}

public sealed class RecordBillableEventValidator : AbstractValidator<RecordBillableEventCommand>
{
    public RecordBillableEventValidator()
    {
        RuleFor(x => x.TenantId).GreaterThan(0);
        RuleFor(x => x.Meter).NotEmpty().MaximumLength(BillableEvent.MeterMaxLength);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(BillableEvent.IdempotencyKeyMaxLength);
        RuleFor(x => x.Description).MaximumLength(BillableEvent.DescriptionMaxLength);
    }
}

public class RecordBillableEventHandler : IRequestHandler<RecordBillableEventCommand, Result<int>>
{
    private readonly IBillableEventRepository _events;
    private readonly IBillingPeriodRepository _periods;
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;

    public RecordBillableEventHandler(
        IBillableEventRepository events, IBillingPeriodRepository periods,
        ITenantRepository tenants, IUnitOfWork uow)
    {
        _events = events; _periods = periods; _tenants = tenants; _uow = uow;
    }

    public async Task<Result<int>> Handle(RecordBillableEventCommand cmd, CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(cmd.TenantId, ct) is null)
            return Result<int>.Failure(InvoiceErrors.TenantNotFound);

        // المفتاحُ أولاً: تكرارٌ معروف يعود بمعرّفه القائم — **نجاحٌ لا تعارض**. من يُعيد
        // المحاولة يريد أن تكون الوحدةُ مسجَّلةً مرّةً، وقد صارت.
        if (await _events.FindByKeyAsync(cmd.TenantId, cmd.IdempotencyKey, ct) is { } existing)
            return Result<int>.Success(existing.Id);

        var period = await _periods.FindCoveringAsync(cmd.TenantId, cmd.OccurredAtUtc, ct);

        // ثلاثةُ أجوبةٍ لا جوابان: فترةٌ مفتوحة تقبله، وفترةٌ لم تعد مفتوحة تردّه **باسم حالتها**،
        // ولا فترةَ أصلاً تُفتح له. والوسطى هي التي يسهل أن تُخلط بالأخيرة — وخلطُها يُنشئ فترةً
        // جديدة تحمل تاريخَ فترةٍ أُغلقت فاتورتُها.
        if (period is not null && !period.AcceptsEvents)
            return Result<int>.Failure(Error.BusinessRule("BillingPeriodClosed",
                "فترةُ الفوترة التي تسع لحظةَ هذا الحدث لم تعد تقبل أحداثاً — الحدثُ المتأخّر يذهب إلى فترةٍ لاحقة"));

        if (period is null)
        {
            period = MonthOf(cmd.TenantId, cmd.OccurredAtUtc);
            await _periods.AddAsync(period, ct);
            await _uow.SaveChangesAsync(ct);
        }

        var billable = new BillableEvent(
            cmd.TenantId, cmd.Meter, cmd.Quantity, cmd.OccurredAtUtc, cmd.IdempotencyKey, period, cmd.Description);
        await _events.AddAsync(billable, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(billable.Id);
    }

    // فترةُ الشهر التقويميّ بالتوقيت العالميّ: تبدأ أوّلَ الشهر وتنتهي أوّلَ الذي يليه (حصرياً).
    // **شهرٌ تقويميّ لا ثلاثون يوماً**: الفواتيرُ الشهرية تُقرأ بأسماء الشهور، ودورةٌ منزلقة كانت
    // ستجعل «فاتورة أيلول» تعني مدّتين مختلفتين في عامين.
    private static BillingPeriod MonthOf(int tenantId, DateTime instant)
    {
        var start = new DateTime(instant.Year, instant.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return new BillingPeriod(tenantId, start, start.AddMonths(1));
    }
}

public record ListBillingPeriodsQuery(int TenantId) : IRequest<IReadOnlyList<BillingPeriodDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.periods.listed", TenantId: TenantId);
}

public class ListBillingPeriodsHandler : IRequestHandler<ListBillingPeriodsQuery, IReadOnlyList<BillingPeriodDto>>
{
    private readonly IPlatformBillingQueries _queries;
    public ListBillingPeriodsHandler(IPlatformBillingQueries queries) => _queries = queries;

    public Task<IReadOnlyList<BillingPeriodDto>> Handle(ListBillingPeriodsQuery q, CancellationToken ct) =>
        _queries.ListPeriodsAsync(q.TenantId, ct);
}

public record ListBillableEventsQuery(int TenantId, int BillingPeriodId)
    : IRequest<IReadOnlyList<BillableEventDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.events.listed", "BillingPeriod",
        BillingPeriodId.ToString(), TenantId);
}

public class ListBillableEventsHandler : IRequestHandler<ListBillableEventsQuery, IReadOnlyList<BillableEventDto>>
{
    private readonly IPlatformBillingQueries _queries;
    public ListBillableEventsHandler(IPlatformBillingQueries queries) => _queries = queries;

    public Task<IReadOnlyList<BillableEventDto>> Handle(ListBillableEventsQuery q, CancellationToken ct) =>
        _queries.ListEventsAsync(q.TenantId, q.BillingPeriodId, ct);
}

// ============================================================================
// إغلاقُ فترة: `BeginClose` ثمّ `Close` في معاملةٍ واحدة.
//
// والحالةُ الوسطى ليست زينةً هنا: لو انقطع شيءٌ بين الاثنتين بقيت الفترةُ «تُغلَق» — لا تقبل
// أحداثاً جديدة، وأرقامُها غيرُ نهائية — فيُستأنف الإغلاق بلا أن تعود تقبل بأثرٍ رجعيّ. وذلك
// بالضبط ما يجعلها ثلاثَ حالاتٍ لا اثنتين.
// ============================================================================
public record CloseBillingPeriodCommand(int TenantId, int BillingPeriodId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.period.closed", "BillingPeriod",
        BillingPeriodId.ToString(), TenantId);
}

public class CloseBillingPeriodHandler : IRequestHandler<CloseBillingPeriodCommand, Result>
{
    private readonly IBillingPeriodRepository _periods;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public CloseBillingPeriodHandler(IBillingPeriodRepository periods, IUnitOfWork uow, TimeProvider clock)
    {
        _periods = periods; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(CloseBillingPeriodCommand cmd, CancellationToken ct)
    {
        var period = await _periods.GetForTenantAsync(cmd.BillingPeriodId, cmd.TenantId, ct);
        if (period is null) return Result.Failure(InvoiceErrors.PeriodNotFound);

        await _uow.InTransactionAsync(async () =>
        {
            period.BeginClose();
            await _uow.SaveChangesAsync(ct);
            period.Close(_clock.GetUtcNow().UtcDateTime);
            await _uow.SaveChangesAsync(ct);
        }, ct);

        return Result.Success();
    }
}

// ============================================================================
// تحميلُ ما قيس على مسوّدةِ فاتورة: يجمع وحدات مقياسٍ بعينه من فترةٍ **مغلقة**، ويُضيف سطراً
// واحداً بسعرٍ يُمرّره المشغّل، ويَسِم الأحداثَ بأنها فُوتِرت.
//
// **ومن فترةٍ مغلقة حصراً.** فترةٌ ما زالت تقبل أحداثاً تُنتج فاتورةً ينقصها ما سيصل بعد دقيقة
// — والفاتورةُ الناقصة تُصحَّح بإشعارٍ أو بفاتورةٍ ثانية، وكلاهما عملٌ لا يُفترض أن يقع لأنّ
// أحداً استعجل.
//
// **والوسمُ في المعاملة نفسها**: وحدةٌ تظهر في فاتورتين مالٌ يُطالَب به مرّتين، و`BillOn` ترمي
// على الثانية بدل أن تتجاهل.
// ============================================================================
public record AddMeteredLinesCommand(
    int InvoiceId, int BillingPeriodId, string Meter, decimal UnitAmount, string? Description)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.metered.added", "PlatformInvoice",
        InvoiceId.ToString(), Metadata: InvoiceErrors.Meta(
            ("billingPeriodId", BillingPeriodId), ("meter", Meter), ("unitAmount", UnitAmount)));
}

public sealed class AddMeteredLinesValidator : AbstractValidator<AddMeteredLinesCommand>
{
    public AddMeteredLinesValidator()
    {
        RuleFor(x => x.Meter).NotEmpty().MaximumLength(BillableEvent.MeterMaxLength);
        RuleFor(x => x.UnitAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Description).MaximumLength(PlatformInvoiceLine.DescriptionMaxLength);
    }
}

public class AddMeteredLinesHandler : IRequestHandler<AddMeteredLinesCommand, Result<int>>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IBillingPeriodRepository _periods;
    private readonly IBillableEventRepository _events;
    private readonly IUnitOfWork _uow;

    public AddMeteredLinesHandler(
        IPlatformInvoiceRepository invoices, IBillingPeriodRepository periods,
        IBillableEventRepository events, IUnitOfWork uow)
    {
        _invoices = invoices; _periods = periods; _events = events; _uow = uow;
    }

    public async Task<Result<int>> Handle(AddMeteredLinesCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetWithDetailsAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result<int>.Failure(InvoiceErrors.InvoiceNotFound);

        var period = await _periods.GetForTenantAsync(cmd.BillingPeriodId, invoice.TenantId, ct);
        if (period is null) return Result<int>.Failure(InvoiceErrors.PeriodNotFound);
        if (period.Status != BillingPeriodStatus.Closed)
            return Result<int>.Failure(Error.BusinessRule("BillingPeriodNotClosed",
                "أغلِق الفترةَ قبل تحميل وحداتها — فترةٌ ما زالت تقبل أحداثاً تُنتج فاتورةً ناقصة"));

        var meter = cmd.Meter.Trim().ToLowerInvariant();
        var unbilled = (await _events.ListUnbilledAsync(invoice.TenantId, period.Id, ct))
            .Where(e => string.Equals(e.Meter, meter, StringComparison.Ordinal))
            .ToList();

        if (unbilled.Count == 0)
            return Result<int>.Failure(Error.BusinessRule("NoUnbilledUnits",
                "لا وحداتٍ غيرَ مفوترة لهذا المقياس في هذه الفترة"));

        var quantity = unbilled.Sum(e => e.Quantity);
        var description = cmd.Description?.Trim() is { Length: > 0 } text ? text : meter;

        // **السطرُ ووسمُ الأحداث معاً أو لا شيء.** حفظتان بلا معاملة تسمحان بسطرٍ يُضاف وأحداثٍ
        // تبقى غيرَ مفوترة، فتُحمَّل ثانيةً في المحاولة التالية ويُطالَب التاجر بالوحدة مرّتين.
        var line = await _uow.InTransactionAsync(async () =>
        {
            var added = invoice.AddLine(description, quantity, new Money(cmd.UnitAmount, invoice.Currency));
            await _uow.SaveChangesAsync(ct);   // السطرُ يحتاج معرّفَه قبل أن تُوسَم الأحداثُ به

            foreach (var billable in unbilled) billable.BillOn(invoice.Id);
            await _uow.SaveChangesAsync(ct);
            return added;
        }, ct);

        return Result<int>.Success(line.Id);
    }
}
