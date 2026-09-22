using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Features.Tax.Contracts;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Billing;

// ============================================================================
// فواتيرُ اشتراكات التجّار والتحصيلُ اليدويّ (C5، [ADR-0056](0056)).
//
// **هذا هو المسارُ الذي تُقبَض به سوق.** قرار المالك `D-13` = A يجعل كلَّ متجرٍ تاجرَ نفسه ولا
// يضع سوق في مسار مال المتسوّق إطلاقاً؛ فإيرادُ سوق اشتراكٌ تُفوتره، ولا طريقَ آخر.
//
// **ولا مزوّدَ دفعٍ هنا بالمرّة.** التحصيلُ حوالةٌ بنكية يراها المشغّل في كشف حسابه ويسجّلها —
// وهو في هذا السوق الحالةُ الرئيسة لا الاستثناء (`C-15`). والحدُّ بين هذا وبين وحدة Payments
// حدٌّ مقصود: تلك بنطاق متجرٍ وسجلّاتُها مرتبطة بطلب، وهذه بنطاق المنصّة وبلا طلبٍ أصلاً.
//
// **وكلُّ أمرٍ هنا مُدقَّق** — منطقةُ منصّة، والمالُ مسؤوليةٌ يجب أن يُعرف مَن حرّكها.
// ============================================================================

internal static class InvoiceErrors
{
    public static Error InvoiceNotFound => Error.NotFound("الفاتورة غير موجودة");
    public static Error TenantNotFound => Error.NotFound("المتجر غير موجود");
    public static Error PeriodNotFound => Error.NotFound("فترةُ الفوترة غير موجودة");

    public static Dictionary<string, object?> Meta(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}

// ── القراءة ─────────────────────────────────────────────────────────────────

public record ListPlatformInvoicesQuery(
    int? TenantId = null, string? Search = null, string? Status = null, bool OverdueOnly = false,
    int Page = 1, int PageSize = 20)
    : IRequest<PaginatedList<PlatformInvoiceSummaryDto>>, IPagedQuery, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoices.listed", TenantId: TenantId);
}

public sealed class ListPlatformInvoicesValidator : PagedQueryValidator<ListPlatformInvoicesQuery>
{
    public ListPlatformInvoicesValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<PlatformInvoiceStatus>(s, ignoreCase: true, out _))
            .WithMessage("حالةُ الفاتورة غير معروفة");
    }
}

public class ListPlatformInvoicesHandler
    : IRequestHandler<ListPlatformInvoicesQuery, PaginatedList<PlatformInvoiceSummaryDto>>
{
    private readonly IPlatformBillingQueries _queries;
    private readonly TimeProvider _clock;

    public ListPlatformInvoicesHandler(IPlatformBillingQueries queries, TimeProvider clock)
    {
        _queries = queries; _clock = clock;
    }

    public Task<PaginatedList<PlatformInvoiceSummaryDto>> Handle(
        ListPlatformInvoicesQuery q, CancellationToken ct)
    {
        PlatformInvoiceStatus? status = Enum.TryParse<PlatformInvoiceStatus>(q.Status, ignoreCase: true, out var parsed)
            ? parsed : null;
        var filter = new PlatformInvoiceListFilter(q.TenantId, q.Search?.Trim(), status, q.OverdueOnly);
        return _queries.ListInvoicesAsync(filter, PageRequest.From(q), _clock.GetUtcNow().UtcDateTime, ct);
    }
}

public record GetPlatformInvoiceQuery(int InvoiceId) : IRequest<Result<PlatformInvoiceDetailDto>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.viewed", "PlatformInvoice", InvoiceId.ToString());
}

public class GetPlatformInvoiceHandler : IRequestHandler<GetPlatformInvoiceQuery, Result<PlatformInvoiceDetailDto>>
{
    private readonly IPlatformBillingQueries _queries;
    private readonly TimeProvider _clock;

    public GetPlatformInvoiceHandler(IPlatformBillingQueries queries, TimeProvider clock)
    {
        _queries = queries; _clock = clock;
    }

    public async Task<Result<PlatformInvoiceDetailDto>> Handle(GetPlatformInvoiceQuery q, CancellationToken ct) =>
        // `tenantId: null` — المنصّة تقرأ أيَّ فاتورة؛ قراءةُ التاجر تمرّ بمسارٍ آخر يحمل متجرَه.
        await _queries.GetInvoiceAsync(q.InvoiceId, null, _clock.GetUtcNow().UtcDateTime, ct) is { } invoice
            ? Result<PlatformInvoiceDetailDto>.Success(invoice)
            : Result<PlatformInvoiceDetailDto>.Failure(InvoiceErrors.InvoiceNotFound);
}

// ── المسوّدة ────────────────────────────────────────────────────────────────

// ============================================================================
// مسوّدةُ فاتورةٍ لمتجر. `IncludeSubscription` تُضيف سطرَ اشتراكه من **سعر خطته المجمَّد**: لا
// يُكتب سعرٌ هنا ولا يُخمَّن — إن لم تُسعَّر الخطة فلا سطر، ويُقال ذلك.
//
// وتبدأ مسوّدةً دائماً: الإصدارُ فعلٌ منفصل يسحب رقماً ويُجمّد لقطةً، وجعلُه جزءاً من الإنشاء
// كان سيعني فاتورةً تصدر قبل أن يقرأها أحد.
// ============================================================================
public record CreatePlatformInvoiceCommand(
    int TenantId, DateTime PeriodStartUtc, DateTime PeriodEndUtc,
    bool IncludeSubscription, int? BillingPeriodId, IReadOnlyList<InvoiceLineInput>? Lines)
    : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.drafted", "PlatformInvoice", null, TenantId,
        InvoiceErrors.Meta(
            ("periodStart", PeriodStartUtc), ("periodEnd", PeriodEndUtc),
            ("includeSubscription", IncludeSubscription), ("lines", Lines?.Count ?? 0)));
}

public sealed class CreatePlatformInvoiceValidator : AbstractValidator<CreatePlatformInvoiceCommand>
{
    public CreatePlatformInvoiceValidator()
    {
        RuleFor(x => x.TenantId).GreaterThan(0);
        RuleFor(x => x.PeriodEndUtc).GreaterThan(x => x.PeriodStartUtc)
            .WithMessage("نهايةُ مدّة الفاتورة بعد بدايتها");
        RuleForEach(x => x.Lines).SetValidator(new InvoiceLineInputValidator()).When(x => x.Lines is not null);
    }
}

public sealed class InvoiceLineInputValidator : AbstractValidator<InvoiceLineInput>
{
    public InvoiceLineInputValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(PlatformInvoiceLine.DescriptionMaxLength);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxCategory).MaximumLength(TaxRate.CategoryMaxLength);
    }
}

public class CreatePlatformInvoiceHandler : IRequestHandler<CreatePlatformInvoiceCommand, Result<int>>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IPlatformBillingSettingsRepository _settings;
    private readonly ITenantRepository _tenants;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPlanRepository _plans;
    private readonly IBillingPeriodRepository _periods;
    private readonly IUnitOfWork _uow;

    public CreatePlatformInvoiceHandler(
        IPlatformInvoiceRepository invoices, IPlatformBillingSettingsRepository settings,
        ITenantRepository tenants, ISubscriptionRepository subscriptions, IPlanRepository plans,
        IBillingPeriodRepository periods, IUnitOfWork uow)
    {
        _invoices = invoices; _settings = settings; _tenants = tenants;
        _subscriptions = subscriptions; _plans = plans; _periods = periods; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreatePlatformInvoiceCommand cmd, CancellationToken ct)
    {
        // الإعدادُ يُفحص عند المسوّدة أيضاً، وليس عند الإصدار وحده: مسوّدةٌ بعملةٍ غير مضبوطة
        // لا عملةَ لها أصلاً، وتأجيلُ الفحص كان سيُنتج مسوّدةً لا تُصدَر أبداً.
        var issuable = BillingSettingsGuard.RequireIssuable(await _settings.GetAsync(ct));
        if (!issuable.IsSuccess) return Result<int>.Failure(issuable.Error!);
        var currency = BillingSettingsGuard.CurrencyOf(issuable.Value!);

        if (await _tenants.GetByIdAsync(cmd.TenantId, ct) is null)
            return Result<int>.Failure(InvoiceErrors.TenantNotFound);

        // فترةُ الفوترة — إن رُبطت — يجب أن تكون **لهذا المتجر**: `GetForTenantAsync` تحمل الشرط،
        // فلا تُربط فاتورةُ متجرٍ بفترةِ آخر بتمرير معرّف.
        if (cmd.BillingPeriodId is int periodId
            && await _periods.GetForTenantAsync(periodId, cmd.TenantId, ct) is null)
            return Result<int>.Failure(InvoiceErrors.PeriodNotFound);

        var subscription = cmd.IncludeSubscription ? await _subscriptions.FindByTenantAsync(cmd.TenantId, ct) : null;
        Plan? plan = subscription is not null ? await _plans.GetByIdAsync(subscription.PlanId, ct) : null;

        if (cmd.IncludeSubscription && plan?.Price is null)
            return Result<int>.Failure(Error.BusinessRule("PlanNotPriced",
                "خطةُ هذا المتجر بلا سعر — سعِّر إصدارَ الخطة أو أضف أسطرَ الفاتورة يدوياً"));
        if (plan?.Price is { } planPrice && planPrice.Currency != currency)
            return Result<int>.Failure(Error.BusinessRule("PlanCurrencyMismatch",
                "عملةُ سعر الخطة تخالف عملةَ فوترة المنصّة"));

        var invoice = new PlatformInvoice(
            cmd.TenantId, currency, cmd.PeriodStartUtc, cmd.PeriodEndUtc, plan?.Id, cmd.BillingPeriodId);

        if (plan?.Price is { } price)
            // الوصفُ نصٌّ يبقى مقروءاً بعد تقاعد الخطة: اسمُها وإصدارُها، لا مرجعٌ إليها.
            invoice.AddLine($"{plan.Name} ({plan.Code}/{plan.Version})", 1, price);

        foreach (var line in cmd.Lines ?? [])
            invoice.AddLine(line.Description, line.Quantity, new Money(line.UnitAmount, currency), line.TaxCategory);

        await _invoices.AddAsync(invoice, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(invoice.Id);
    }
}

public record AddPlatformInvoiceLineCommand(int InvoiceId, InvoiceLineInput Line) : IRequest<Result<int>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.line.added", "PlatformInvoice", InvoiceId.ToString(),
        Metadata: InvoiceErrors.Meta(("description", Line?.Description), ("quantity", Line?.Quantity),
            ("unitAmount", Line?.UnitAmount)));
}

public sealed class AddPlatformInvoiceLineValidator : AbstractValidator<AddPlatformInvoiceLineCommand>
{
    public AddPlatformInvoiceLineValidator()
    {
        RuleFor(x => x.Line).NotNull();
        RuleFor(x => x.Line).SetValidator(new InvoiceLineInputValidator()).When(x => x.Line is not null);
    }
}

public class AddPlatformInvoiceLineHandler : IRequestHandler<AddPlatformInvoiceLineCommand, Result<int>>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IUnitOfWork _uow;

    public AddPlatformInvoiceLineHandler(IPlatformInvoiceRepository invoices, IUnitOfWork uow)
    {
        _invoices = invoices; _uow = uow;
    }

    public async Task<Result<int>> Handle(AddPlatformInvoiceLineCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetWithDetailsAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result<int>.Failure(InvoiceErrors.InvoiceNotFound);

        // العملةُ من الفاتورة لا من المدخل: سطرٌ بعملةٍ أخرى يرفضه المجال، وإرسالُها من العميل
        // كان سيجعل الرفضَ رسالةَ تحقّقٍ بدل أن يكون مستحيلاً.
        var line = invoice.AddLine(
            cmd.Line.Description, cmd.Line.Quantity, new Money(cmd.Line.UnitAmount, invoice.Currency),
            cmd.Line.TaxCategory);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(line.Id);
    }
}

public record RemovePlatformInvoiceLineCommand(int InvoiceId, int LineId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.line.removed", "PlatformInvoice",
        InvoiceId.ToString(), Metadata: InvoiceErrors.Meta(("lineId", LineId)));
}

public class RemovePlatformInvoiceLineHandler : IRequestHandler<RemovePlatformInvoiceLineCommand, Result>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IUnitOfWork _uow;

    public RemovePlatformInvoiceLineHandler(IPlatformInvoiceRepository invoices, IUnitOfWork uow)
    {
        _invoices = invoices; _uow = uow;
    }

    public async Task<Result> Handle(RemovePlatformInvoiceLineCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetWithDetailsAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result.Failure(InvoiceErrors.InvoiceNotFound);

        invoice.RemoveLine(cmd.LineId);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public record UpdatePlatformInvoiceNotesCommand(int InvoiceId, string? Notes) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.notes.updated", "PlatformInvoice", InvoiceId.ToString());
}

public sealed class UpdatePlatformInvoiceNotesValidator : AbstractValidator<UpdatePlatformInvoiceNotesCommand>
{
    public UpdatePlatformInvoiceNotesValidator() =>
        RuleFor(x => x.Notes).MaximumLength(PlatformInvoice.NotesMaxLength);
}

public class UpdatePlatformInvoiceNotesHandler : IRequestHandler<UpdatePlatformInvoiceNotesCommand, Result>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IUnitOfWork _uow;

    public UpdatePlatformInvoiceNotesHandler(IPlatformInvoiceRepository invoices, IUnitOfWork uow)
    {
        _invoices = invoices; _uow = uow;
    }

    public async Task<Result> Handle(UpdatePlatformInvoiceNotesCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result.Failure(InvoiceErrors.InvoiceNotFound);

        invoice.SetNotes(cmd.Notes);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// إلغاءُ مسوّدة. الفاتورةُ الصادرة لا تصل إلى هنا — يرفضها المجال، لا هذا المعالج.
public record CancelPlatformInvoiceDraftCommand(int InvoiceId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.draft.cancelled", "PlatformInvoice", InvoiceId.ToString());
}

public class CancelPlatformInvoiceDraftHandler : IRequestHandler<CancelPlatformInvoiceDraftCommand, Result>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IUnitOfWork _uow;

    public CancelPlatformInvoiceDraftHandler(IPlatformInvoiceRepository invoices, IUnitOfWork uow)
    {
        _invoices = invoices; _uow = uow;
    }

    public async Task<Result> Handle(CancelPlatformInvoiceDraftCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result.Failure(InvoiceErrors.InvoiceNotFound);

        invoice.CancelDraft();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ============================================================================
// **الإصدار.** اللحظةُ التي تصير فيها المسوّدةُ مستنداً، وهي كلُّها داخل معاملةٍ واحدة:
//
//   1. يُحتسب أساسُ الضريبة من الأسطر، وتُضرَّب بملفّ **المنصّة** (`QuoteForProfileAsync`) —
//      وبوّابةُ التحقّق المهنيّ تنطبق هنا كما تنطبق على سلّة متسوّق: إصدارٌ لم يؤكّده أحد
//      يُنتج صفراً، وتُجمَّد اللقطةُ قائلةً ذلك.
//   2. يُسحب الرقمُ من السلسلة (`IPlatformDocumentNumbers`) — **داخل المعاملة**، فرقمٌ سُحب
//      لمستندٍ لم يصدر يعود معها ولا يترك فجوة.
//   3. تُجمَّد لقطةُ المُصدِر والمُرسَل إليه وتعليماتِ الدفع، ويُغلق البابُ خلفها.
//
// والشحنُ صفرٌ في الأساس: فاتورةُ اشتراكٍ لا تُشحَن، و`ShippingTaxable` في ملفّ الاختصاص لا
// تجد ما تُضرّبه — وتمريرُ صفرٍ صريحٍ يجعل ذلك مقروءاً بدل أن يكون حذفاً.
// ============================================================================
public record IssuePlatformInvoiceCommand(int InvoiceId, string? BilledToTaxNumber)
    : IRequest<Result<string>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.issued", "PlatformInvoice", InvoiceId.ToString());
}

public sealed class IssuePlatformInvoiceValidator : AbstractValidator<IssuePlatformInvoiceCommand>
{
    public IssuePlatformInvoiceValidator() =>
        RuleFor(x => x.BilledToTaxNumber).MaximumLength(PlatformInvoice.TaxNumberMaxLength);
}

public class IssuePlatformInvoiceHandler : IRequestHandler<IssuePlatformInvoiceCommand, Result<string>>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly IPlatformBillingSettingsRepository _settings;
    private readonly ITenantRepository _tenants;
    private readonly ITaxCalculator _tax;
    private readonly IPlatformDocumentNumbers _numbers;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public IssuePlatformInvoiceHandler(
        IPlatformInvoiceRepository invoices, IPlatformBillingSettingsRepository settings,
        ITenantRepository tenants, ITaxCalculator tax, IPlatformDocumentNumbers numbers,
        IUnitOfWork uow, TimeProvider clock)
    {
        _invoices = invoices; _settings = settings; _tenants = tenants;
        _tax = tax; _numbers = numbers; _uow = uow; _clock = clock;
    }

    public async Task<Result<string>> Handle(IssuePlatformInvoiceCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetWithDetailsAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result<string>.Failure(InvoiceErrors.InvoiceNotFound);

        var issuable = BillingSettingsGuard.RequireIssuable(await _settings.GetAsync(ct));
        if (!issuable.IsSuccess) return Result<string>.Failure(issuable.Error!);
        var settings = issuable.Value!;

        var tenant = await _tenants.GetByIdAsync(invoice.TenantId, ct);
        if (tenant is null) return Result<string>.Failure(InvoiceErrors.TenantNotFound);

        var now = _clock.GetUtcNow().UtcDateTime;

        // اللحظةُ التي تُرجَّح بها قواعدُ الضريبة هي لحظةُ الإصدار — لا «الآن» عند إعادة قراءة،
        // ولا بدايةُ المدّة. الفاتورةُ تنشأ في تاريخ إصدارها، وهي القاعدةُ نفسها في `Order`.
        var quote = await _tax.QuoteForProfileAsync(
            new TaxBasis(invoice.Subtotal, Money.Zero(invoice.Currency)),
            settings.TaxProfileId, settings.TaxCollectionEnabled, now, ct);

        var number = await _uow.InTransactionAsync(async () =>
        {
            var allocated = await _numbers.NextAsync(
                PlatformDocumentSequence.InvoiceSeries, settings.InvoiceNumberPrefix, ct);

            invoice.Issue(
                allocated, now, now.AddDays(settings.PaymentTermsDays),
                quote.Amount, quote.Snapshot,
                settings.IssuerName!, settings.IssuerAddress, settings.IssuerTaxNumber,
                tenant.Name, cmd.BilledToTaxNumber, settings.PaymentInstructions);

            await _uow.SaveChangesAsync(ct);
            return allocated;
        }, ct);

        return Result<string>.Success(number);
    }
}

// ============================================================================
// تسجيلُ سدادٍ وقع خارج النظام. **هذا هو التحصيل** في C5 — لا استدعاءَ مزوّد ولا خطّاف.
//
// ومَن سجّله يُنسَب في الصفّ نفسه لا في سجلّ التدقيق وحده: الفاتورةُ تُقرأ وحدها بعد سنوات،
// وسجلُّ التدقيق يُقرأ بسؤالٍ آخر.
// ============================================================================
public record RecordInvoicePaymentCommand(
    int InvoiceId, decimal Amount, string Method, DateTime ReceivedAtUtc, string? Reference, string? Note)
    : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.invoice.payment.recorded", "PlatformInvoice",
        InvoiceId.ToString(), Metadata: InvoiceErrors.Meta(
            ("amount", Amount), ("method", Method), ("receivedAt", ReceivedAtUtc), ("reference", Reference)));
}

public sealed class RecordInvoicePaymentValidator : AbstractValidator<RecordInvoicePaymentCommand>
{
    public RecordInvoicePaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).NotEmpty()
            .Must(m => Enum.TryParse<PlatformPaymentMethod>(m, ignoreCase: true, out _))
            .WithMessage("طريقةُ السداد غير معروفة");
        RuleFor(x => x.Reference).MaximumLength(PlatformInvoicePayment.ReferenceMaxLength);
        RuleFor(x => x.Note).MaximumLength(PlatformInvoicePayment.NoteMaxLength);
    }
}

public class RecordInvoicePaymentHandler : IRequestHandler<RecordInvoicePaymentCommand, Result>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly ICurrentUser _user;
    private readonly IUnitOfWork _uow;

    public RecordInvoicePaymentHandler(IPlatformInvoiceRepository invoices, ICurrentUser user, IUnitOfWork uow)
    {
        _invoices = invoices; _user = user; _uow = uow;
    }

    public async Task<Result> Handle(RecordInvoicePaymentCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetWithDetailsAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result.Failure(InvoiceErrors.InvoiceNotFound);

        invoice.RecordPayment(
            new Money(cmd.Amount, invoice.Currency),
            Enum.Parse<PlatformPaymentMethod>(cmd.Method, ignoreCase: true),
            cmd.ReceivedAtUtc, _user.RequireUserId(), cmd.Reference, cmd.Note);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ============================================================================
// **إشعارُ دائن**: التصحيحُ الوحيد الممكن على فاتورةٍ صدرت. يُنشَأ ويُصدَر في أمرٍ واحد عمداً —
// إشعارٌ يبقى مسوّدةً لا يُصحّح شيئاً، وتركُه قابلاً للتحرير كان سيجعل «كم على هذا التاجر؟»
// سؤالاً بجوابين.
// ============================================================================
public record IssueCreditNoteCommand(int InvoiceId, string Reason, IReadOnlyList<InvoiceLineInput> Lines)
    : IRequest<Result<string>>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("billing.credit.note.issued", "PlatformInvoice", InvoiceId.ToString(),
        Metadata: InvoiceErrors.Meta(("reason", Reason), ("lines", Lines?.Count ?? 0)));
}

public sealed class IssueCreditNoteValidator : AbstractValidator<IssueCreditNoteCommand>
{
    public IssueCreditNoteValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(CreditNote.ReasonMaxLength);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("إشعارُ الدائن يحتاج سطراً واحداً على الأقل");
        RuleForEach(x => x.Lines).SetValidator(new InvoiceLineInputValidator());
    }
}

public class IssueCreditNoteHandler : IRequestHandler<IssueCreditNoteCommand, Result<string>>
{
    private readonly IPlatformInvoiceRepository _invoices;
    private readonly ICreditNoteRepository _creditNotes;
    private readonly IPlatformBillingSettingsRepository _settings;
    private readonly ITaxCalculator _tax;
    private readonly IPlatformDocumentNumbers _numbers;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public IssueCreditNoteHandler(
        IPlatformInvoiceRepository invoices, ICreditNoteRepository creditNotes,
        IPlatformBillingSettingsRepository settings, ITaxCalculator tax,
        IPlatformDocumentNumbers numbers, IUnitOfWork uow, TimeProvider clock)
    {
        _invoices = invoices; _creditNotes = creditNotes; _settings = settings;
        _tax = tax; _numbers = numbers; _uow = uow; _clock = clock;
    }

    public async Task<Result<string>> Handle(IssueCreditNoteCommand cmd, CancellationToken ct)
    {
        var invoice = await _invoices.GetWithDetailsAsync(cmd.InvoiceId, ct);
        if (invoice is null) return Result<string>.Failure(InvoiceErrors.InvoiceNotFound);

        var issuable = BillingSettingsGuard.RequireIssuable(await _settings.GetAsync(ct));
        if (!issuable.IsSuccess) return Result<string>.Failure(issuable.Error!);
        var settings = issuable.Value!;

        var note = new CreditNote(invoice);
        foreach (var line in cmd.Lines)
            note.AddLine(line.Description, line.Quantity, new Money(line.UnitAmount, invoice.Currency), line.TaxCategory);

        var now = _clock.GetUtcNow().UtcDateTime;

        // لقطةُ الإشعار **لقطتُه هو**، محسوبةً بقواعد لحظة إصداره: إشعارٌ يصدر بعد سنةٍ قد يقع
        // تحت إصدارِ قواعدَ آخر، وافتراضُ أنّ ضريبةَ التصحيح هي ضريبةُ الأصل قاعدةُ اختصاصٍ لا
        // يضعها المهندس (ADR-0055).
        var quote = await _tax.QuoteForProfileAsync(
            new TaxBasis(note.Subtotal, Money.Zero(invoice.Currency)),
            settings.TaxProfileId, settings.TaxCollectionEnabled, now, ct);

        var number = await _uow.InTransactionAsync(async () =>
        {
            var allocated = await _numbers.NextAsync(
                PlatformDocumentSequence.CreditNoteSeries, settings.CreditNoteNumberPrefix, ct);

            note.Issue(invoice, allocated, now, cmd.Reason, quote.Amount, quote.Snapshot);
            await _creditNotes.AddAsync(note, ct);
            await _uow.SaveChangesAsync(ct);
            return allocated;
        }, ct);

        return Result<string>.Success(number);
    }
}
