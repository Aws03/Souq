using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Platform;

// ============================================================================
// إشعارُ دائنٍ على فاتورةِ منصّة: **تجميعةٌ منفصلة** لا تعديلٌ على الفاتورة ([ADR-0056](0056)،
// CommercialPlatformArchitecture §4.6). جدولُ منصّةٍ بمفتاح متجر (الشكل B) كفاتورته.
//
// **ولماذا تجميعةٌ منفصلة ولِمَ لا يُعدَّل المستندُ الصادر؟** لأنّ هذا هو الفرقُ بين دفترٍ
// محاسبيّ وجدولٍ في قاعدة بيانات. الفاتورةُ التي خرجت وصلت إلى تاجرٍ وقد تكون دخلت دفترَه ونظامَ
// ضريبةٍ في اختصاصه؛ فتخفيضُ مبلغها في مكانها يجعل نسختَين من مستندٍ واحد تحملان الرقمَ نفسه
// ورقمَين مختلفَين. والتصحيحُ في كل نظامٍ محاسبيّ حقيقيّ مستندٌ **ثانٍ** يشير إلى الأول.
//
// **وله سلسلةُ ترقيمٍ خاصّة به** لا سلسلةُ الفواتير: مستندان مختلفان لا يتشاركان تسلسلاً.
//
// **ولقطةُ ضريبته لقطتُه هو**، لا نسخةٌ من لقطة الفاتورة: إشعارٌ يصدر بعد سنةٍ من فاتورته قد
// يقع تحت إصدارِ قواعدَ آخر، وافتراضُ أنّ ضريبةَ التصحيح هي ضريبةُ الأصل قاعدةُ اختصاصٍ لا
// يضعها المهندس (ADR-0055). فمن يُصدره يمرّر اللقطة التي حُسبت له.
// ============================================================================
public enum CreditNoteStatus
{
    Draft = 0,
    Issued = 1,
}

public class CreditNote : Entity
{
    public const int NumberMaxLength = 40;
    public const int ReasonMaxLength = 500;
    public const int MaxLines = 200;

    private readonly List<CreditNoteLine> _lines = new();

    public int TenantId { get; private set; }
    public int PlatformInvoiceId { get; private set; }

    public string? Number { get; private set; }
    public CreditNoteStatus Status { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTime? IssuedAtUtc { get; private set; }

    // سببُ الإشعار. **مطلوبٌ عند الإصدار** لا اختياريّ: إشعارٌ بلا سبب يُقرأ بعد سنتين ولا يُعرف
    // لماذا خُفِّض مستحقّ، وهو أوّلُ ما يُسأل عنه في مراجعة.
    public string? Reason { get; private set; }

    public decimal TaxAmount { get; private set; }
    public TaxSnapshot? TaxSnapshot { get; private set; }

    public IReadOnlyCollection<CreditNoteLine> Lines => _lines.AsReadOnly();

    private CreditNote() { }

    public CreditNote(PlatformInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (!invoice.IsIssued)
            throw new InvalidCreditNoteException("لا يُنشَأ إشعارُ دائنٍ إلا على فاتورةٍ صادرة");

        TenantId = invoice.TenantId;
        PlatformInvoiceId = invoice.Id;
        Currency = invoice.Currency;
        Status = CreditNoteStatus.Draft;
    }

    public Money Subtotal =>
        _lines.Aggregate(Money.Zero(Currency), (sum, line) => sum.Add(line.LineTotal));

    public Money Tax => new(TaxAmount, Currency);

    public Money TaxAddedToTotal =>
        TaxSnapshot?.PriceMode == TaxPriceMode.Exclusive ? Tax : Money.Zero(Currency);

    public Money Total => Subtotal.Add(TaxAddedToTotal);

    public CreditNoteLine AddLine(string description, decimal quantity, Money unitAmount, string? taxCategory = null)
    {
        RequireDraft("أسطرُ إشعار الدائن");
        if (_lines.Count >= MaxLines)
            throw new InvalidCreditNoteException($"حتى {MaxLines} سطراً لإشعار الدائن");
        if (unitAmount.Currency != Currency)
            throw new InvalidCreditNoteException("عملةُ السطر تخالف عملةَ إشعار الدائن");

        var line = new CreditNoteLine(description, quantity, unitAmount, taxCategory);
        _lines.Add(line);
        return line;
    }

    // ============================================================================
    // الإصدار: يأخذ رقمَه، ويُجمّد لقطتَه، **ويُقيّد مبلغَه على فاتورته في الفعل نفسه**.
    //
    // وتمريرُ الفاتورة هنا مقصود: `ApplyCredit` داخليّة، فلا يستطيع مسارٌ أن يُصدر إشعاراً بلا
    // أن يُنقص مستحقّ فاتورته، ولا أن يُنقص مستحقّاً بلا إشعارٍ يفسّره. الفعلان واحدٌ بالبناء لا
    // باتّفاقٍ بين معالجَين.
    // ============================================================================
    public void Issue(PlatformInvoice invoice, string number, DateTime issuedAtUtc, string reason,
        Money taxAmount, TaxSnapshot? taxSnapshot)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        RequireDraft("إصدارُ إشعار الدائن");
        if (invoice.Id != PlatformInvoiceId)
            throw new InvalidCreditNoteException("الإشعارُ وفاتورتُه لا يتطابقان");
        if (_lines.Count == 0)
            throw new InvalidCreditNoteException("لا يُصدَر إشعارُ دائنٍ بلا سطر واحد");
        if (taxAmount.Currency != Currency)
            throw new InvalidCreditNoteException("عملةُ الضريبة تخالف عملةَ إشعار الدائن");
        if (taxSnapshot is null && taxAmount.Amount != 0)
            throw new InvalidCreditNoteException("مبلغُ ضريبةٍ بلا لقطةٍ تُفسّره");
        if (taxSnapshot is not null && taxSnapshot.TotalAmount != taxAmount.Amount)
            throw new InvalidCreditNoteException("مبلغُ الضريبة لا يساوي مجموعَ أسطر لقطته");

        var trimmedReason = PlatformInvoice.Normalize(reason, ReasonMaxLength, "سببُ إشعار الدائن", allowNewLines: true)
            ?? throw new InvalidCreditNoteException("إشعارُ دائنٍ بلا سبب");

        TaxAmount = taxAmount.Amount;
        TaxSnapshot = taxSnapshot;

        // القيدُ أولاً: يرفض ما يتجاوز المتبقّي، فلا يأخذ الإشعارُ رقماً من السلسلة ثم يفشل —
        // ورقمٌ يُسحب لمستندٍ لا يصدر فجوةٌ في سلسلةٍ يُفترض أنّها متّصلة.
        invoice.ApplyCredit(Total);

        Number = NormalizeNumber(number);
        IssuedAtUtc = issuedAtUtc;
        Reason = trimmedReason;
        Status = CreditNoteStatus.Issued;
    }

    private void RequireDraft(string what)
    {
        if (Status != CreditNoteStatus.Draft)
            throw new InvalidCreditNoteException($"لا يُعدَّل {what} بعد الإصدار");
    }

    private static string NormalizeNumber(string? number)
    {
        var trimmed = number?.Trim() ?? "";
        if (trimmed.Length is 0 or > NumberMaxLength)
            throw new InvalidCreditNoteException($"رقمُ إشعار الدائن مطلوب، حتى {NumberMaxLength} محرفاً");
        return trimmed;
    }
}

// سطرُ إشعار دائن. نظيرُ `PlatformInvoiceLine` بحذافيره، ونوعٌ مستقلّ لا مشترك: جدولان
// مستقلّان لمستندَين مستقلّين، ومشاركةُ الجدول كانت ستجعل حذفَ أحدهما يمسّ الآخر.
public class CreditNoteLine : Entity
{
    public const int DescriptionMaxLength = 300;

    public int CreditNoteId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Quantity { get; private set; }
    public decimal UnitAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string TaxCategory { get; private set; } = default!;

    public Money UnitPrice => new(UnitAmount, Currency);

    public Money LineTotal => Money.FromCalculation(Quantity * UnitAmount, Currency);

    private CreditNoteLine() { }

    internal CreditNoteLine(string description, decimal quantity, Money unitAmount, string? taxCategory)
    {
        Description = PlatformInvoice.Normalize(description, DescriptionMaxLength, "وصفُ السطر")
            ?? throw new InvalidCreditNoteException("سطرٌ بلا وصف");
        if (quantity <= 0)
            throw new InvalidCreditNoteException("كمّيةُ السطر أكبر من صفر");
        Quantity = quantity;
        UnitAmount = unitAmount.Amount;
        Currency = unitAmount.Currency;
        TaxCategory = taxCategory?.Trim() is { Length: > 0 } category ? category : TaxRate.DefaultCategory;
        if (TaxCategory.Length > TaxRate.CategoryMaxLength)
            throw new InvalidCreditNoteException($"فئةُ ضريبة السطر حتى {TaxRate.CategoryMaxLength} حرفاً");
    }
}
