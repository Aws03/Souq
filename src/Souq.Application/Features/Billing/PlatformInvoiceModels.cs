using Souq.Application.Common.Models;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Billing;

// ============================================================================
// عقودُ فوترة التاجر كما تُقرأ وتُكتب (C5، [ADR-0056](0056)).
//
// **الفاتورةُ تُقرأ من نفسها.** كلُّ ما تحتاجه شاشةٌ أو مراجعةٌ أو تصديرٌ محاسبيّ موجودٌ في هذا
// الشكل: المُصدِرُ والمُرسَلُ إليه والأسطرُ ولقطةُ الضريبة وما سُدّد وما قُيّد دائناً — ولا وصلةَ
// إلى صفٍّ قد يتغيّر بعد سنة. وذلك هو الفرقُ بين مستندٍ وسجلٍّ في جدول.
//
// **وحالةُ التحقّق الضريبيّ تظهر حيث يظهر الرقم** — القاعدةُ نفسها التي يفرضها ADR-0055 على كل
// شكلٍ يُقرأ منه معدَّل: فاتورةٌ صدرت بإصدارٍ أكّده محاسبٌ تقول ذلك، وأخرى صدرت بصفرٍ لأنّ أحداً
// لم يؤكّده تقول ذلك أيضاً.
// ============================================================================

public sealed record PlatformInvoiceLineDto(
    int Id, string Description, decimal Quantity, decimal UnitAmount, decimal LineTotal, string TaxCategory);

public sealed record PlatformInvoicePaymentDto(
    int Id, decimal Amount, string Currency, string Method, DateTime ReceivedAtUtc,
    int RecordedByUserId, string? RecordedByEmail, string? Reference, string? Note);

public sealed record TaxSnapshotLineDto(string Code, string Name, int BasisPoints, decimal Amount);

// لقطةُ الضريبة المجمَّدة كما تُقرأ. تحمل حالةَ التحقّق التي كانت **لحظتَها** لا حالتَها اليوم:
// سحبُ تحقّقٍ بعد الإصدار لا يُغيّر ما تقوله فاتورةٌ صدرت.
public sealed record TaxSnapshotDto(
    int TaxProfileId, int Version, string Jurisdiction, string PriceMode,
    string Verification, bool ShippingTaxable, decimal TotalAmount, int TotalBasisPoints,
    IReadOnlyList<TaxSnapshotLineDto> Lines);

public sealed record CreditNoteDto(
    int Id, string? Number, string Status, string Currency, DateTime? IssuedAtUtc, string? Reason,
    decimal Subtotal, decimal TaxAmount, decimal Total,
    IReadOnlyList<PlatformInvoiceLineDto> Lines, TaxSnapshotDto? TaxSnapshot);

// صفُّ القائمة: ما يُقرأ في جدول بلا تحميل الأسطر. `Outstanding` و`IsOverdue` محتسبان في
// الخادم لا في الواجهة — الواجهةُ تعرض ولا تقرّر (FrontendGuide).
public sealed record PlatformInvoiceSummaryDto(
    int Id, int TenantId, string? TenantName, string? Number, string Status, string Currency,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime? IssuedAtUtc, DateTime? DueAtUtc,
    decimal Subtotal, decimal TaxAmount, decimal Total, decimal AmountPaid, decimal Credited,
    decimal Outstanding, bool IsOverdue, int DaysOverdue);

public sealed record PlatformInvoiceDetailDto(
    int Id, int TenantId, string? TenantName, string? Number, string Status, string Currency,
    int? PlanId, string? PlanName, int? BillingPeriodId,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime? IssuedAtUtc, DateTime? DueAtUtc,
    string? IssuerName, string? IssuerAddress, string? IssuerTaxNumber,
    string? BilledToName, string? BilledToTaxNumber,
    string? PaymentInstructions, string? Notes,
    decimal Subtotal, decimal TaxAmount, decimal Total, decimal AmountPaid, decimal Credited,
    decimal Outstanding, bool IsOverdue, int DaysOverdue,
    IReadOnlyList<PlatformInvoiceLineDto> Lines,
    IReadOnlyList<PlatformInvoicePaymentDto> Payments,
    IReadOnlyList<CreditNoteDto> CreditNotes,
    TaxSnapshotDto? TaxSnapshot);

// ما يراه المشغّل عن إعداد فوترته — **وما ينقصه**. `CanIssue` و`BlockingReason` معاً: زرٌّ
// معطَّل بلا سببٍ مكتوب يُنتج بلاغاً، لا إعداداً مكتملاً.
public sealed record PlatformBillingSettingsDto(
    string? Currency, string? IssuerName, string? IssuerAddress, string? IssuerTaxNumber,
    string InvoiceNumberPrefix, string CreditNoteNumberPrefix,
    int PaymentTermsDays, int GracePeriodDays, string? PaymentInstructions,
    int? TaxProfileId, string? TaxJurisdiction, string? TaxProfileName, bool TaxCollectionEnabled,
    bool CanIssue, string? BlockingReason, string TaxReason);

public sealed record PlatformBillingSettingsInput(
    string? Currency, string? IssuerName, string? IssuerAddress, string? IssuerTaxNumber,
    string? InvoiceNumberPrefix, string? CreditNoteNumberPrefix,
    int PaymentTermsDays, int GracePeriodDays, string? PaymentInstructions,
    int? TaxProfileId, bool TaxCollectionEnabled);

public sealed record InvoiceLineInput(string Description, decimal Quantity, decimal UnitAmount, string? TaxCategory);

public sealed record BillingPeriodDto(
    int Id, int TenantId, DateTime StartsAtUtc, DateTime EndsAtUtc, string Status,
    DateTime? ClosedAtUtc, int EventCount, int UnbilledEventCount);

public sealed record BillableEventDto(
    int Id, int TenantId, string Meter, decimal Quantity, DateTime OccurredAtUtc,
    string IdempotencyKey, string? Description, int BillingPeriodId, int? PlatformInvoiceId);

// ما يراه **التاجر** عن اشتراكه: خطتُه وسعرُها، وما عليه، ومتى. لا يرى إعدادَ المنصّة ولا
// فواتيرَ غيره.
public sealed record MySubscriptionDto(
    string? PlanCode, string? PlanName, int? PlanVersion, string? SubscriptionStatus,
    DateTime? SubscribedAtUtc, decimal? PriceAmount, string? PriceCurrency, int BillingIntervalMonths,
    decimal OutstandingTotal, string? OutstandingCurrency, int OpenInvoiceCount, int OverdueInvoiceCount,
    string? PaymentInstructions);

public sealed record PlatformInvoiceListFilter(
    int? TenantId, string? Search, PlatformInvoiceStatus? Status, bool OverdueOnly)
{
    // ما صدر وحده. يستعمله مسارُ التاجر: مسوّدةٌ يحرّرها مشغّلٌ الآن ليست مطالبةً بعد، ومسوّدةٌ
    // أُلغيت لم تكن مطالبةً قطّ. والترشيحُ خاصيّةٌ في المرشّح لا شرطٌ يُنسَخ في كل مُنادٍ.
    public bool IssuedOnly { get; init; }
}

// ============================================================================
// منفذُ القراءة لفوترة التاجر (ADR-0008). تنفيذُه داخليٌّ في Infrastructure، وهو من الأنواع
// المراجَعة المسموح لها بقراءة جداول المنصّة ذات مفتاح المتجر: كل قراءةٍ تخصّ متجراً تحمل شرط
// `TenantId` صريحاً، والقراءةُ العابرة للمتاجر لا تقع إلّا في مسار المنصّة المُدقَّق.
// ============================================================================
public interface IPlatformBillingQueries
{
    Task<PaginatedList<PlatformInvoiceSummaryDto>> ListInvoicesAsync(
        PlatformInvoiceListFilter filter, PageRequest page, DateTime utcNow, CancellationToken ct);

    Task<PlatformInvoiceDetailDto?> GetInvoiceAsync(int invoiceId, int? tenantId, DateTime utcNow, CancellationToken ct);

    Task<IReadOnlyList<BillingPeriodDto>> ListPeriodsAsync(int tenantId, CancellationToken ct);

    Task<IReadOnlyList<BillableEventDto>> ListEventsAsync(int tenantId, int billingPeriodId, CancellationToken ct);

    Task<MySubscriptionDto> GetSubscriptionSummaryAsync(int tenantId, DateTime utcNow, CancellationToken ct);

    // هل صدرت فاتورةٌ واحدة على الإطلاق؟ سؤالٌ عابرٌ للمتاجر عمداً — يقرّر إن كانت عملةُ
    // الفوترة ما تزال قابلةً للتغيير. مجموعٌ لا صفوف، فهو من القراءات التي يسمح بها عُرفُ
    // `PlatformQueries` عبر المتاجر.
    Task<bool> AnyIssuedInvoiceAsync(CancellationToken ct);
}

// ============================================================================
// أسبابُ تعذُّر الإصدار، برموزٍ ثابتة تتفرّع عليها الواجهة (ADR-0017) — النمطُ نفسه الذي تتبعه
// `TaxCollectionReasons`، ولنفس السبب: «لا تستطيع الإصدار» بلا سببٍ مسمّى تُقرأ كعطب.
// ============================================================================
public static class BillingBlockingReasons
{
    public const string None = "None";
    public const string SettingsMissing = "BillingSettingsMissing";
    public const string CurrencyNotSet = "BillingCurrencyNotSet";
    public const string IssuerNotSet = "BillingIssuerNotSet";
}

public static class PlatformBillingMapper
{
    public static PlatformInvoiceLineDto ToDto(PlatformInvoiceLine line) => new(
        line.Id, line.Description, line.Quantity, line.UnitAmount, line.LineTotal.Amount, line.TaxCategory);

    public static PlatformInvoiceLineDto ToDto(CreditNoteLine line) => new(
        line.Id, line.Description, line.Quantity, line.UnitAmount, line.LineTotal.Amount, line.TaxCategory);

    public static TaxSnapshotDto? ToDto(Souq.Domain.ValueObjects.TaxSnapshot? snapshot) => snapshot is null ? null
        : new TaxSnapshotDto(
            snapshot.TaxProfileId, snapshot.Version, snapshot.Jurisdiction,
            snapshot.PriceMode.ToString(), snapshot.Verification.ToString(), snapshot.ShippingTaxable,
            snapshot.TotalAmount, snapshot.TotalBasisPoints,
            snapshot.Lines.Select(l => new TaxSnapshotLineDto(l.Code, l.Name, l.BasisPoints, l.Amount)).ToList());

    public static CreditNoteDto ToDto(CreditNote note) => new(
        note.Id, note.Number, note.Status.ToString(), note.Currency, note.IssuedAtUtc, note.Reason,
        note.Subtotal.Amount, note.TaxAmount, note.Total.Amount,
        note.Lines.Select(ToDto).ToList(), ToDto(note.TaxSnapshot));
}
