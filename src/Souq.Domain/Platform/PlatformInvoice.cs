using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Platform;

// ============================================================================
// حالةُ فاتورةِ المنصّة — **بمفردات سوق نفسها** لا بمفردات مزوّد، للسبب الذي يسوقه
// CommercialPlatformArchitecture §4.6: مفرداتُ المزوّدين تتناقض، وواحدٌ من تناقضاتها خطير.
// ولا مزوّدَ هنا أصلاً: التحصيلُ يدويّ بقرار `C-15`.
//
//   • Draft     — تُحرَّر: تُضاف أسطرُها وتُحذف. بلا رقم، ولا وجودَ لها محاسبيّاً.
//   • Issued    — صدرت: أخذت رقمَها من السلسلة، وجُمّدت لقطةُ ضريبتها ومُصدِرُها ومُرسَلٌ إليه.
//                 **لا يُعدَّل فيها حرفٌ بعد ذلك**، والتصحيحُ إشعارُ دائن.
//   • Settled   — لم يبقَ عليها شيء: سُدّدت، أو قُيّدت دائناً، أو الاثنان معاً.
//   • Cancelled — **مسوّدةٌ** أُلغيت. ولا تصل إليها فاتورةٌ صدرت أبداً.
//
// **ولماذا لا حالةَ «متأخّرة» مخزَّنة؟** لأنّ التأخّر دالّةُ زمنٍ لا فعلُ أحد: فاتورةٌ تصير
// متأخّرةً بمرور منتصف ليلٍ لا بأمرٍ يُنفَّذ. وتخزينُه يعني صفّاً يكذب حتى تمرّ عليه وظيفةٌ —
// وهو بالضبط نوعُ الكذب الذي يظهر في شاشةِ تاجرٍ قبل أن يظهر في سجلّ. فـ `IsOverdue` تُحتسب من
// `DueAtUtc` والمتبقّي، و**المطالبةُ الآلية وتصعيدُها إلى تعليقٍ هما C6** لا هذا الملفّ.
// ============================================================================
public enum PlatformInvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Settled = 2,
    Cancelled = 3,
}

// ============================================================================
// طريقةُ تحصيلٍ **بلا مزوّد**: قرار المالك `C-15` يجعل التحويل البنكيّ الحالةَ الرئيسة في هذا
// السوق لا الاستثناء، و`D-13` = A يجعل اشتراكَ التاجر مصدرَ إيراد سوق الوحيد. فالقائمةُ هنا
// تصف **ما يفعله إنسانٌ خارج النظام** ويُسجّله مشغّلٌ بعد وقوعه — لا تكاملاً مع أحد.
// ============================================================================
public enum PlatformPaymentMethod
{
    BankTransfer = 0,
    Cash = 1,
    Cheque = 2,
    Other = 3,
}

// ============================================================================
// فاتورةُ اشتراكٍ تُصدِرها سوق لتاجر: جدولُ منصّةٍ **بمفتاح متجر** (الشكل B في ADR-0047 §1)
// ([ADR-0056](0056)).
//
// **ولماذا الشكل B لا الشكل A؟** لأنّها ليست بيانات التاجر بل دفترُ سوق عنه — ولأنّها يجب أن
// **تبقى مقروءةً بعد أرشفة المتجر**: تاجرٌ توقّف وعليه مستحقّ لا تختفي فاتورتُه بأرشفته، ومرشّحُ
// المستأجر كان سيُخفيها عن المنصّة نفسها.
//
// **وهي منفصلةٌ تماماً عن مال المتسوّق** (وحدة Payments): ذاك مسارٌ بنطاق متجرٍ وسجلّاته مرتبطة
// بطلب، وهذا بنطاق المنصّة وبلا طلبٍ أصلاً (CommercialPlatformArchitecture §5.1). لا يلتقيان،
// ولا يُقرَأ أحدهما في حساب الآخر.
//
// ── ما يُجمَّد عند الإصدار، وكلُّه لسببٍ واحد ────────────────────────────────
// الفاتورةُ مستندٌ يُقرأ بعد سنواتٍ في مراجعةٍ أو خلاف، فما تقوله يجب أن يبقى صحيحاً بمعزلٍ عن
// كلّ صفٍّ آخر في القاعدة. لذلك تُنسَخ عليها لحظةَ الإصدار: اسمُ المُصدِر وعنوانُه ورقمُه
// الضريبيّ، واسمُ المتجر ورقمُه الضريبيّ، وتعليماتُ الدفع، ولقطةُ الضريبة كاملةً بقيمها. متجرٌ
// غيّر اسمَه لا يُغيّر فاتورةً صدرت، ومنصّةٌ غيّرت عنوانَها لا تُعيد كتابة تاريخها.
// ============================================================================
public class PlatformInvoice : Entity
{
    public const int NumberMaxLength = 40;
    public const int NameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int TaxNumberMaxLength = 60;
    public const int NotesMaxLength = 2000;
    public const int MaxLines = 200;
    public const int MaxPayments = 100;

    private readonly List<PlatformInvoiceLine> _lines = new();
    private readonly List<PlatformInvoicePayment> _payments = new();

    public int TenantId { get; private set; }

    // `null` حتى الإصدار. الرقمُ يُسحب من السلسلة في معاملة الإصدار نفسها ولا يتغيّر بعدها أبداً
    // — لا بتحرير، ولا بإعادة إصدار، ولا بأيّ مسارٍ في هذا الصنف.
    public string? Number { get; private set; }

    public PlatformInvoiceStatus Status { get; private set; }
    public string Currency { get; private set; } = default!;

    // الخطةُ التي فُوتِرت، إن كانت الفاتورة عن اشتراك. مرجعٌ للتتبّع: اسمُ الخطة وسعرُها يعيشان
    // في **سطر** الفاتورة، فلا تتغيّر قراءةُ الفاتورة لو تقاعدت الخطة.
    public int? PlanId { get; private set; }

    public int? BillingPeriodId { get; private set; }

    // المدّةُ التي تغطّيها الفاتورة. نهايةٌ حصريّة كفترة الفوترة، وللسبب نفسه.
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }

    public DateTime? IssuedAtUtc { get; private set; }
    public DateTime? DueAtUtc { get; private set; }

    public string? IssuerName { get; private set; }
    public string? IssuerAddress { get; private set; }
    public string? IssuerTaxNumber { get; private set; }

    public string? BilledToName { get; private set; }
    public string? BilledToTaxNumber { get; private set; }

    public string? PaymentInstructions { get; private set; }
    public string? Notes { get; private set; }

    public decimal TaxAmount { get; private set; }
    public TaxSnapshot? TaxSnapshot { get; private set; }

    // ما قُيّد دائناً على هذه الفاتورة. يزيده إصدارُ إشعار دائن، ولا ينقص أبداً: إشعارٌ صدر لا
    // يُسحب — يُقابَل بفاتورةٍ جديدة إن لزم.
    public decimal CreditedAmount { get; private set; }

    // ── المطالبة (C6، [ADR-0058](0058)) ─────────────────────────────────────
    // **حالةُ المطالبة تعيش على المستند نفسه، لا في جدولٍ موازٍ.** «كم مرّةً ذُكِّر هذا التاجر
    // بهذه الفاتورة ومتى» سؤالٌ عن الفاتورة، وجوابُه في مكانٍ آخر يفترق عنها يوماً.

    public int RemindersSent { get; private set; }

    public DateTime? LastReminderAtUtc { get; private set; }

    // متى تسبّبت هذه الفاتورة في تعليق متجرها. **مرّةً واحدة أبداً**: التعليقُ فعلٌ يقع على المتجر
    // لا على الفاتورة، وتكرارُه بلا معنى — والعلامةُ هنا هي ما يمنع سلّم المطالبة من إعادته في
    // كل دورة.
    public DateTime? EscalatedAtUtc { get; private set; }

    public IReadOnlyCollection<PlatformInvoiceLine> Lines => _lines.AsReadOnly();
    public IReadOnlyCollection<PlatformInvoicePayment> Payments => _payments.AsReadOnly();

    private PlatformInvoice() { }

    public PlatformInvoice(
        int tenantId, string currency, DateTime periodStartUtc, DateTime periodEndUtc,
        int? planId = null, int? billingPeriodId = null)
    {
        if (tenantId <= 0) throw new InvalidPlatformInvoiceException("فاتورةٌ بلا متجر");
        if (periodEndUtc <= periodStartUtc)
            throw new InvalidPlatformInvoiceException("نهايةُ مدّة الفاتورة بعد بدايتها");

        TenantId = tenantId;
        Currency = Money.Zero(currency).Currency;   // يتحقّق من الرمز ويوحّد صيغته
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        PlanId = planId;
        BillingPeriodId = billingPeriodId;
        Status = PlatformInvoiceStatus.Draft;
    }

    // ── المبالغ: كلُّها محتسبةٌ من الأسطر، ولا واحدَ منها مخزَّنٌ بجانبها ──────────
    // القاعدةُ هنا هي قاعدةُ `Order` نفسها: مجموعٌ يُحفظ بجانب أسطره يفترق عنها يوماً، والفرقُ
    // لا يُكتشف إلّا حين يعترض تاجر.

    public Money Subtotal =>
        _lines.Aggregate(Money.Zero(Currency), (sum, line) => sum.Add(line.LineTotal));

    public Money Tax => new(TaxAmount, Currency);

    // الضريبةُ تُضاف إلى الإجمالي في العُرف «المضاف» وحده؛ في «الشامل» هي داخل الأسعار أصلاً.
    // القاعدةُ نفسها في `Order.TaxAddedToTotal` و`TaxQuote.AddedToTotal` — ثلاثةُ مواضع تقول
    // الشيء نفسه لأنّ كلاًّ منها يملك حسابَه، وعكسُها في أحدها يُنتج رقماً أكبر بنسبة الضريبة.
    public Money TaxAddedToTotal =>
        TaxSnapshot?.PriceMode == TaxPriceMode.Exclusive ? Tax : Money.Zero(Currency);

    public Money Total => Subtotal.Add(TaxAddedToTotal);

    public Money AmountPaid =>
        _payments.Aggregate(Money.Zero(Currency), (sum, payment) => sum.Add(payment.Value));

    public Money Credited => new(CreditedAmount, Currency);

    // المتبقّي لا يكون سالباً: كلُّ مسارٍ يزيد المسدَّد أو المقيَّد يرفض التجاوز، فالطرحُ آمن.
    public Money Outstanding => Total.Subtract(AmountPaid).Subtract(Credited);

    public bool IsIssued => Status is PlatformInvoiceStatus.Issued or PlatformInvoiceStatus.Settled;

    // متأخّرة: صدرت، وبقي عليها شيء، ومرّ استحقاقُها. دالّةُ لحظةٍ تُسأل، لا حقلٌ يُكتب — انظر
    // رأس التعداد أعلاه.
    public bool IsOverdueAt(DateTime utcNow) =>
        Status == PlatformInvoiceStatus.Issued && DueAtUtc is { } due && utcNow > due
        && Outstanding.Amount > 0;

    // الأيامُ منذ الاستحقاق. تُقرأ في الشاشة، وستكون مدخلَ سلّم المطالبة في C6.
    public int DaysOverdueAt(DateTime utcNow) =>
        IsOverdueAt(utcNow) ? (int)Math.Floor((utcNow - DueAtUtc!.Value).TotalDays) : 0;

    // ── التحرير: المسوّدة وحدها ────────────────────────────────────────────────

    public PlatformInvoiceLine AddLine(string description, decimal quantity, Money unitAmount, string? taxCategory = null)
    {
        RequireDraft("أسطرُ الفاتورة");
        if (_lines.Count >= MaxLines)
            throw new InvalidPlatformInvoiceException($"حتى {MaxLines} سطراً للفاتورة");
        if (unitAmount.Currency != Currency)
            throw new InvalidPlatformInvoiceException("عملةُ السطر تخالف عملةَ الفاتورة");

        var line = new PlatformInvoiceLine(description, quantity, unitAmount, taxCategory);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(int lineId)
    {
        RequireDraft("أسطرُ الفاتورة");
        var line = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new InvalidPlatformInvoiceException("سطرُ الفاتورة غير موجود");
        _lines.Remove(line);
    }

    public void SetNotes(string? notes)
    {
        RequireDraft("ملاحظاتُ الفاتورة");
        Notes = Normalize(notes, NotesMaxLength, "ملاحظات الفاتورة", allowNewLines: true);
    }

    // إلغاءُ **مسوّدة**. الفاتورةُ الصادرة لا تُلغى أبداً: مستندٌ خرج لا يُمحى، ويُقابَل بإشعار
    // دائن — وهذا هو الفرق بين دفترٍ وجدول.
    public void CancelDraft()
    {
        RequireDraft("إلغاءُ الفاتورة");
        Status = PlatformInvoiceStatus.Cancelled;
    }

    // ============================================================================
    // الإصدار: اللحظةُ التي تصير فيها الفاتورة مستنداً.
    //
    // يأخذ الرقمَ جاهزاً من مُنادِيه لأنّ سحبَه من السلسلة فعلُ قاعدةٍ داخل معاملة — والمجالُ لا
    // يعرف معاملات. ويجمّد كلَّ ما يُقرأ من خارجه، ثم يُغلق البابَ خلفه.
    //
    // **ومبلغُ الضريبة يجب أن تُنتجه أسطرُ لقطته**: مبلغٌ بلا قواعدَ وراءه لا يُخزَّن هنا، وهي
    // القاعدةُ التي يفرضها `Order.ApplyTax` حرفياً — فلا يستطيع مسارٌ أن يكتب ضريبةً لا تفسّرها
    // الفاتورة نفسها.
    // ============================================================================
    public void Issue(
        string number, DateTime issuedAtUtc, DateTime dueAtUtc,
        Money taxAmount, TaxSnapshot? taxSnapshot,
        string issuerName, string? issuerAddress, string? issuerTaxNumber,
        string billedToName, string? billedToTaxNumber, string? paymentInstructions)
    {
        RequireDraft("إصدارُ الفاتورة");
        if (_lines.Count == 0)
            throw new InvalidPlatformInvoiceException("لا تُصدَر فاتورةٌ بلا سطر واحد");
        if (dueAtUtc < issuedAtUtc)
            throw new InvalidPlatformInvoiceException("تاريخُ الاستحقاق لا يسبق تاريخ الإصدار");
        if (taxAmount.Currency != Currency)
            throw new InvalidPlatformInvoiceException("عملةُ الضريبة تخالف عملةَ الفاتورة");
        if (taxSnapshot is null && taxAmount.Amount != 0)
            throw new InvalidPlatformInvoiceException("مبلغُ ضريبةٍ بلا لقطةٍ تُفسّره");
        if (taxSnapshot is not null && taxSnapshot.TotalAmount != taxAmount.Amount)
            throw new InvalidPlatformInvoiceException("مبلغُ الضريبة لا يساوي مجموعَ أسطر لقطته");

        Number = NormalizeNumber(number);
        IssuedAtUtc = issuedAtUtc;
        DueAtUtc = dueAtUtc;
        TaxAmount = taxAmount.Amount;
        TaxSnapshot = taxSnapshot;

        IssuerName = Normalize(issuerName, NameMaxLength, "اسم المُصدِر")
            ?? throw new InvalidPlatformInvoiceException("فاتورةٌ بلا اسم مُصدِر — اضبط إعدادَ فوترة المنصّة أولاً");
        IssuerAddress = Normalize(issuerAddress, AddressMaxLength, "عنوان المُصدِر", allowNewLines: true);
        IssuerTaxNumber = Normalize(issuerTaxNumber, TaxNumberMaxLength, "الرقم الضريبيّ للمُصدِر");

        BilledToName = Normalize(billedToName, NameMaxLength, "اسم المُرسَل إليه")
            ?? throw new InvalidPlatformInvoiceException("فاتورةٌ بلا اسم مُرسَلٍ إليه");
        BilledToTaxNumber = Normalize(billedToTaxNumber, TaxNumberMaxLength, "الرقم الضريبيّ للمُرسَل إليه");

        PaymentInstructions = Normalize(paymentInstructions, PlatformBillingSettings.PaymentInstructionsMaxLength,
            "تعليمات الدفع", allowNewLines: true);

        Status = PlatformInvoiceStatus.Issued;

        // فاتورةٌ بصفرٍ (خطةٌ مجّانية، أو أسطرٌ مجموعُها صفر) تُعَدّ مسدَّدةً فور صدورها: تركُها
        // «مفتوحة» أبداً كان سيجعلها تظهر في كل قائمة مستحقّات ولا تُغلَق بفعلٍ ممكن.
        SettleIfNothingOutstanding();
    }

    // ============================================================================
    // تسجيلُ سدادٍ وقع **خارج النظام**: حوالةٌ بنكية أو نقدٌ أو شيك، يسجّله مشغّلُ المنصّة بعد أن
    // يراه في كشف حسابه. هذا هو كلُّ «التحصيل» في C5، وهو المقصود بقرار `C-15`.
    //
    // **والسدادُ الجزئيّ مسموح** لأنّه يقع فعلاً: تاجرٌ يحوّل نصفَ المبلغ اليوم ونصفَه الشهر
    // القادم واقعةٌ عادية في هذا السوق، ورفضُها كان سيدفع المشغّل إلى «تقريب» الأرقام يدوياً.
    //
    // **ويُرفض ما يتجاوز المتبقّي**، لا يُقبَل ويُطرح: رصيدٌ دائنٌ لتاجرٍ على فاتورة مفهومٌ مختلف
    // لم يقرّره أحد، وخلقُه ضمناً هنا كان قراراً تجارياً بالصدفة.
    // ============================================================================
    public PlatformInvoicePayment RecordPayment(
        Money amount, PlatformPaymentMethod method, DateTime receivedAtUtc,
        int recordedByUserId, string? reference, string? note)
    {
        if (Status is not (PlatformInvoiceStatus.Issued or PlatformInvoiceStatus.Settled))
            throw new InvalidPlatformInvoiceException("لا يُسجَّل سدادٌ إلا على فاتورةٍ صادرة");
        if (_payments.Count >= MaxPayments)
            throw new InvalidPlatformInvoiceException($"حتى {MaxPayments} سداداً للفاتورة");
        if (amount.Currency != Currency)
            throw new InvalidPlatformInvoiceException("عملةُ السداد تخالف عملةَ الفاتورة");
        if (amount.Amount <= 0)
            throw new InvalidPlatformInvoiceException("مبلغُ السداد أكبر من صفر");
        if (amount.Amount > Outstanding.Amount)
            throw new InvalidPlatformInvoiceException(
                $"مبلغُ السداد يتجاوز المتبقّي على الفاتورة ({Outstanding})");
        if (recordedByUserId <= 0)
            throw new InvalidPlatformInvoiceException("سدادٌ بلا مَن سجّله");

        var payment = new PlatformInvoicePayment(amount, method, receivedAtUtc, recordedByUserId, reference, note);
        _payments.Add(payment);
        SettleIfNothingOutstanding();
        return payment;
    }

    // يُنادى من إصدار إشعار الدائن، في معاملته نفسها. `internal` كي لا يستطيع مسارٌ آخر أن
    // يُنقص مستحقّاً بلا مستندٍ يفسّره.
    internal void ApplyCredit(Money amount)
    {
        if (!IsIssued)
            throw new InvalidCreditNoteException("لا يُقيَّد دائنٌ إلا على فاتورةٍ صادرة");
        if (amount.Currency != Currency)
            throw new InvalidCreditNoteException("عملةُ إشعار الدائن تخالف عملةَ الفاتورة");
        if (amount.Amount <= 0)
            throw new InvalidCreditNoteException("مبلغُ إشعار الدائن أكبر من صفر");
        if (amount.Amount > Outstanding.Amount)
            throw new InvalidCreditNoteException(
                $"مبلغُ إشعار الدائن يتجاوز المتبقّي على الفاتورة ({Outstanding})");

        CreditedAmount += amount.Amount;
        SettleIfNothingOutstanding();
    }

    // ============================================================================
    // تسجيلُ تذكيرٍ أُرسل. لا يُرسل شيئاً — الإرسالُ من شأن المُنادي وصندوق الصادر — بل يسجّل أنّه
    // أُرسل، وهو ما يمنع إرسالَه كلَّ دورةٍ بعد ذلك.
    //
    // ولا يُسجَّل على مستندٍ لم يعد عليه شيء: تذكيرٌ بفاتورةٍ سُدّدت مطالبةٌ بمالٍ وصل.
    // ============================================================================
    public void RecordReminder(DateTime utcNow)
    {
        if (Status != PlatformInvoiceStatus.Issued)
            throw new InvalidPlatformInvoiceException("لا يُذكَّر إلّا بفاتورةٍ صادرةٍ لم تُسدَّد");
        RemindersSent++;
        LastReminderAtUtc = utcNow;
    }

    // ============================================================================
    // تسجيلُ أنّ هذه الفاتورة صعّدت إلى تعليق. **مرّةً واحدة**، ومحاولةُ الثانية ترمي: سلّمٌ يُعيد
    // التعليق كلَّ دورة يُغرق سجلّ التدقيق ويُرسل إشعاراً لا جديد فيه، ويجعل «متى عُلِّق هذا
    // المتجر؟» سؤالاً بلا جواب واحد.
    // ============================================================================
    public void RecordEscalation(DateTime utcNow)
    {
        if (Status != PlatformInvoiceStatus.Issued)
            throw new InvalidPlatformInvoiceException("لا يُصعَّد إلّا بفاتورةٍ صادرةٍ لم تُسدَّد");
        if (EscalatedAtUtc is not null)
            throw new InvalidPlatformInvoiceException("صُعِّدت هذه الفاتورة من قبل — ولا تُصعَّد مرّتين");
        EscalatedAtUtc = utcNow;
    }

    private void SettleIfNothingOutstanding()
    {
        if (Outstanding.Amount == 0) Status = PlatformInvoiceStatus.Settled;
    }

    private void RequireDraft(string what)
    {
        if (Status != PlatformInvoiceStatus.Draft)
            throw new InvalidPlatformInvoiceException(
                $"لا يُعدَّل {what} بعد الإصدار — المستندُ الصادر يُقابَل بإشعار دائن لا يُحرَّر");
    }

    private static string NormalizeNumber(string? number)
    {
        var trimmed = number?.Trim() ?? "";
        if (trimmed.Length is 0 or > NumberMaxLength)
            throw new InvalidPlatformInvoiceException($"رقمُ الفاتورة مطلوب، حتى {NumberMaxLength} محرفاً");
        return trimmed;
    }

    internal static string? Normalize(string? value, int maxLength, string field, bool allowNewLines = false)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            throw new InvalidPlatformInvoiceException($"{field} حتى {maxLength} حرفاً");
        if (trimmed.Any(c => char.IsControl(c) && !(allowNewLines && c is '\n' or '\r')))
            throw new InvalidPlatformInvoiceException($"{field} يحتوي محارف غير مسموحة");
        return trimmed;
    }
}

// ============================================================================
// سطرُ فاتورة. يحمل **نصَّ** ما فُوتِر لا مرجعاً إليه: «خطة كذا، الإصدار 3، شهر أيلول» نصٌّ يبقى
// مقروءاً بعد أن تتقاعد الخطة ويتغيّر اسمُها — وهو السببُ نفسه الذي يجعل `OrderItem` يحمل اسمَ
// المنتج لا معرّفَه وحده.
// ============================================================================
public class PlatformInvoiceLine : Entity
{
    public const int DescriptionMaxLength = 300;

    public int PlatformInvoiceId { get; private set; }
    public string Description { get; private set; } = default!;

    // كمّيةٌ عشرية لا صحيحة: وحدةُ مقياسٍ قد تكون جزءاً (ساعاتُ تخزين، غيغابايت)، وتقريبُها إلى
    // صحيحٍ عند الفوترة يُنتج فرقاً في كل فاتورة.
    public decimal Quantity { get; private set; }
    public decimal UnitAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string TaxCategory { get; private set; } = default!;

    public Money UnitPrice => new(UnitAmount, Currency);

    // مجموعُ السطر بتقريبٍ واحد في الموضع المسموح به (`Money.FromCalculation`، ADR-0014): كمّيةٌ
    // عشرية مضروبةٌ في سعرٍ تُنتج كسوراً أدقّ من فلس، والتقريبُ هنا مرّةً واحدة لا في كل مجموع.
    public Money LineTotal => Money.FromCalculation(Quantity * UnitAmount, Currency);

    private PlatformInvoiceLine() { }

    internal PlatformInvoiceLine(string description, decimal quantity, Money unitAmount, string? taxCategory)
    {
        Description = PlatformInvoice.Normalize(description, DescriptionMaxLength, "وصفُ السطر")
            ?? throw new InvalidPlatformInvoiceException("سطرٌ بلا وصف");
        if (quantity <= 0)
            throw new InvalidPlatformInvoiceException("كمّيةُ السطر أكبر من صفر");
        Quantity = quantity;
        UnitAmount = unitAmount.Amount;
        Currency = unitAmount.Currency;
        TaxCategory = (taxCategory?.Trim() is { Length: > 0 } category ? category : TaxRate.DefaultCategory);
        if (TaxCategory.Length > TaxRate.CategoryMaxLength)
            throw new InvalidPlatformInvoiceException($"فئةُ ضريبة السطر حتى {TaxRate.CategoryMaxLength} حرفاً");
    }
}

// ============================================================================
// سدادٌ مسجَّل يدوياً. **سجلٌّ لا يُعدَّل**: تصحيحُ سدادٍ أُدخل خطأً ليس تحريراً لصفّه — وذلك
// مقصودٌ ومكتوبٌ هنا كي لا يُضاف `Edit` لاحقاً بلا أن ينتبه أحد إلى ما يعنيه.
//
// ويحمل **مَن سجّله**: التحصيلُ اليدويّ يعني أنّ إنساناً أقرّ بوصول مال، وذلك إقرارٌ يجب أن
// يُنسَب — وهو فوق سجلّ التدقيق لا بديلٌ عنه، لأنّ الفاتورة تُقرأ وحدها بعد سنوات.
// ============================================================================
public class PlatformInvoicePayment : Entity
{
    public const int ReferenceMaxLength = 120;
    public const int NoteMaxLength = 500;

    public int PlatformInvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public PlatformPaymentMethod Method { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public int RecordedByUserId { get; private set; }

    // مرجعُ الحوالة كما هو في كشف البنك. هو ما يُطابَق عليه عند خلاف، فلا يُفسَّر ولا يُطبَّع
    // إلّا تشذيباً.
    public string? Reference { get; private set; }
    public string? Note { get; private set; }

    public Money Value => new(Amount, Currency);

    private PlatformInvoicePayment() { }

    internal PlatformInvoicePayment(
        Money amount, PlatformPaymentMethod method, DateTime receivedAtUtc,
        int recordedByUserId, string? reference, string? note)
    {
        Amount = amount.Amount;
        Currency = amount.Currency;
        Method = method;
        ReceivedAtUtc = receivedAtUtc;
        RecordedByUserId = recordedByUserId;
        Reference = PlatformInvoice.Normalize(reference, ReferenceMaxLength, "مرجعُ السداد");
        Note = PlatformInvoice.Normalize(note, NoteMaxLength, "ملاحظةُ السداد", allowNewLines: true);
    }
}
