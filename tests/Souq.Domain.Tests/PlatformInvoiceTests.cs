using AwesomeAssertions;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Tests;

// ============================================================================
// فاتورةُ اشتراكِ التاجر وإشعارُ دائنها (C5، ADR-0056).
//
// **أهمُّ ما يُختبر هنا هو ما يستحيل فعلُه**: تعديلُ فاتورةٍ صدرت، وتسجيلُ سدادٍ يتجاوز المتبقّي،
// وتخزينُ مبلغِ ضريبةٍ لا تُنتجه أسطرُ لقطته، وإلغاءُ مستندٍ خرج. فقرار المالك `D-13` = A جعل
// هذه الفواتيرَ مصدرَ إيراد سوق الوحيد — فما يضمن صحّتَها ليس انضباطَ مَن يكتب المعالج.
// ============================================================================
public class PlatformInvoiceTests
{
    private const string Jod = "JOD";
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Money Jd(decimal amount) => new(amount, Jod);

    private static PlatformInvoice Draft(decimal unit = 30m, int quantity = 1)
    {
        var invoice = new PlatformInvoice(7, Jod, Start, End);
        invoice.AddLine("اشتراك أيلول", quantity, Jd(unit));
        return invoice;
    }

    private static TaxSnapshot Snapshot(decimal amount, TaxPriceMode mode = TaxPriceMode.Exclusive) =>
        new(1, 1, "JO", mode, TaxVerificationState.Verified, false,
            [new TaxSnapshotLine("standard", "Standard", 1600, amount)]);

    private static PlatformInvoice Issued(decimal unit = 30m, decimal tax = 0m, TaxSnapshot? snapshot = null)
    {
        var invoice = Draft(unit);
        invoice.Issue("INV000001", Now, Now.AddDays(30), Jd(tax), snapshot,
            "سوق", "عمّان", "TX-1", "متجر التجربة", "TX-9", "حوّل إلى الحساب رقم 1");
        return invoice;
    }

    // ── الإصدار يُجمّد ─────────────────────────────────────────────────────────

    [Fact]
    public void المسوّدة_بلا_رقم_والإصدار_يمنحها_رقمها_ولا_يُعاد()
    {
        var invoice = Draft();
        invoice.Number.Should().BeNull();
        invoice.Status.Should().Be(PlatformInvoiceStatus.Draft);

        invoice.Issue("INV000042", Now, Now.AddDays(30), Jd(0), null,
            "سوق", null, null, "متجر التجربة", null, null);

        invoice.Number.Should().Be("INV000042");
        invoice.Status.Should().Be(PlatformInvoiceStatus.Issued);

        // إصدارٌ ثانٍ مستحيل: الرقمُ الذي أُعطي لا يُستبدَل بآخر.
        var again = () => invoice.Issue("INV000043", Now, Now.AddDays(30), Jd(0), null,
            "سوق", null, null, "متجر التجربة", null, null);
        again.Should().Throw<InvalidPlatformInvoiceException>();
        invoice.Number.Should().Be("INV000042");
    }

    [Fact]
    public void المستند_الصادر_لا_يُحرَّر_ولا_يُلغى()
    {
        var invoice = Issued();

        var addLine = () => invoice.AddLine("سطرٌ بعد الإصدار", 1, Jd(5));
        var removeLine = () => invoice.RemoveLine(invoice.Lines.First().Id);
        var setNotes = () => invoice.SetNotes("ملاحظة");
        var cancel = () => invoice.CancelDraft();

        addLine.Should().Throw<InvalidPlatformInvoiceException>();
        removeLine.Should().Throw<InvalidPlatformInvoiceException>();
        setNotes.Should().Throw<InvalidPlatformInvoiceException>();
        // **الإلغاء هو الأهمّ**: مستندٌ خرج يُقابَل بإشعار دائن، ولا يُمحى.
        cancel.Should().Throw<InvalidPlatformInvoiceException>();
    }

    [Fact]
    public void لا_تُصدَر_فاتورة_بلا_سطر_ولا_باستحقاق_يسبق_إصدارها()
    {
        var empty = new PlatformInvoice(7, Jod, Start, End);
        var issueEmpty = () => empty.Issue("INV1", Now, Now, Jd(0), null, "سوق", null, null, "متجر", null, null);
        issueEmpty.Should().Throw<InvalidPlatformInvoiceException>();

        var invoice = Draft();
        var backdated = () => invoice.Issue("INV1", Now, Now.AddDays(-1), Jd(0), null,
            "سوق", null, null, "متجر", null, null);
        backdated.Should().Throw<InvalidPlatformInvoiceException>();
    }

    [Fact]
    public void فاتورة_بلا_مُصدِر_أو_بلا_مُرسَل_إليه_لا_تصدر()
    {
        var noIssuer = () => Draft().Issue("INV1", Now, Now, Jd(0), null, "  ", null, null, "متجر", null, null);
        var noRecipient = () => Draft().Issue("INV1", Now, Now, Jd(0), null, "سوق", null, null, " ", null, null);

        noIssuer.Should().Throw<InvalidPlatformInvoiceException>();
        noRecipient.Should().Throw<InvalidPlatformInvoiceException>();
    }

    // ── الضريبة: لا مبلغَ بلا قواعدَ وراءه ──────────────────────────────────────

    [Fact]
    public void مبلغ_ضريبة_لا_تُنتجه_أسطر_لقطته_مرفوض()
    {
        // القاعدةُ نفسها التي يفرضها `Order.ApplyTax`: مبلغٌ بلا قواعدَ أنتجته لا يُخزَّن.
        var mismatched = () => Draft().Issue("INV1", Now, Now, Jd(5), Snapshot(4.8m),
            "سوق", null, null, "متجر", null, null);
        mismatched.Should().Throw<InvalidPlatformInvoiceException>();

        // ومبلغٌ بلا لقطةٍ أصلاً مرفوضٌ كذلك.
        var orphan = () => Draft().Issue("INV1", Now, Now, Jd(5), null,
            "سوق", null, null, "متجر", null, null);
        orphan.Should().Throw<InvalidPlatformInvoiceException>();
    }

    [Fact]
    public void الضريبة_تُضاف_في_المضاف_ولا_تُضاف_في_الشامل()
    {
        // مضاف: الإجمالي يتجاوز مجموعَ الأسطر بمقدار الضريبة.
        var exclusive = Issued(100m, 16m, Snapshot(16m));
        exclusive.Subtotal.Amount.Should().Be(100m);
        exclusive.Total.Amount.Should().Be(116m);

        // شامل: الضريبةُ داخل الأسعار، فالإجمالي هو مجموعُ الأسطر نفسه — وعكسُ هذا يُضاعف
        // المطالبة بمقدار الضريبة على كل فاتورة.
        var inclusive = Issued(100m, 13.793m, Snapshot(13.793m, TaxPriceMode.Inclusive));
        inclusive.Subtotal.Amount.Should().Be(100m);
        inclusive.Total.Amount.Should().Be(100m);
    }

    // ── التحصيل اليدويّ ────────────────────────────────────────────────────────

    [Fact]
    public void السداد_الجزئي_مقبول_والمتبقّي_ينقص_والحالة_تتبعه()
    {
        var invoice = Issued(100m);
        invoice.Outstanding.Amount.Should().Be(100m);

        invoice.RecordPayment(Jd(40), PlatformPaymentMethod.BankTransfer, Now, 3, "REF-1", null);
        invoice.AmountPaid.Amount.Should().Be(40m);
        invoice.Outstanding.Amount.Should().Be(60m);
        invoice.Status.Should().Be(PlatformInvoiceStatus.Issued);

        invoice.RecordPayment(Jd(60), PlatformPaymentMethod.Cash, Now, 3, null, null);
        invoice.Outstanding.Amount.Should().Be(0m);
        invoice.Status.Should().Be(PlatformInvoiceStatus.Settled);
    }

    [Fact]
    public void سداد_يتجاوز_المتبقّي_مرفوض_ولا_يُقبَل_ويُطرَح()
    {
        var invoice = Issued(100m);
        invoice.RecordPayment(Jd(90), PlatformPaymentMethod.BankTransfer, Now, 3, null, null);

        // رصيدٌ دائنٌ على فاتورة مفهومٌ لم يقرّره أحد، وخلقُه ضمناً قرارٌ تجاريّ بالصدفة.
        var overpay = () => invoice.RecordPayment(Jd(20), PlatformPaymentMethod.Cash, Now, 3, null, null);
        overpay.Should().Throw<InvalidPlatformInvoiceException>();
        invoice.AmountPaid.Amount.Should().Be(90m);
    }

    [Fact]
    public void لا_سداد_على_مسوّدة_ولا_سداد_بلا_مَن_سجّله()
    {
        var draft = Draft();
        var onDraft = () => draft.RecordPayment(Jd(10), PlatformPaymentMethod.Cash, Now, 3, null, null);
        onDraft.Should().Throw<InvalidPlatformInvoiceException>();

        var invoice = Issued();
        var anonymous = () => invoice.RecordPayment(Jd(10), PlatformPaymentMethod.Cash, Now, 0, null, null);
        anonymous.Should().Throw<InvalidPlatformInvoiceException>();
    }

    [Fact]
    public void فاتورة_بصفر_تُعَدّ_مسدَّدة_فور_صدورها()
    {
        // وإلّا بقيت «مفتوحة» أبداً في كل قائمة مستحقّات، بلا فعلٍ ممكن يُغلقها.
        var invoice = new PlatformInvoice(7, Jod, Start, End);
        invoice.AddLine("خطةٌ مجّانية", 1, Jd(0));
        invoice.Issue("INV1", Now, Now, Jd(0), null, "سوق", null, null, "متجر", null, null);

        invoice.Total.Amount.Should().Be(0m);
        invoice.Status.Should().Be(PlatformInvoiceStatus.Settled);
    }

    [Fact]
    public void التأخّر_يُحتسب_ولا_يُخزَّن()
    {
        var invoice = Issued(100m);          // الاستحقاق بعد ثلاثين يوماً

        invoice.IsOverdueAt(Now.AddDays(29)).Should().BeFalse();
        invoice.IsOverdueAt(Now.AddDays(31)).Should().BeTrue();
        invoice.DaysOverdueAt(Now.AddDays(31)).Should().Be(1);

        // ومسدَّدةٌ لا تتأخّر مهما مرّ الزمن: الحالةُ صارت `Settled` فلا شيء عليها.
        invoice.RecordPayment(Jd(100), PlatformPaymentMethod.BankTransfer, Now, 3, null, null);
        invoice.IsOverdueAt(Now.AddDays(400)).Should().BeFalse();
    }

    // ── إشعار الدائن ──────────────────────────────────────────────────────────

    [Fact]
    public void إشعار_الدائن_يُنقص_المتبقّي_ويُسدّد_الفاتورة_حين_يستغرقه()
    {
        var invoice = Issued(100m);
        var note = new CreditNote(invoice);
        note.AddLine("تصحيحُ سطرٍ زائد", 1, Jd(100));

        note.Issue(invoice, "CN000001", Now, "خطأٌ في احتساب المدّة", Jd(0), null);

        note.Status.Should().Be(CreditNoteStatus.Issued);
        note.Number.Should().Be("CN000001");
        invoice.CreditedAmount.Should().Be(100m);
        invoice.Outstanding.Amount.Should().Be(0m);
        invoice.Status.Should().Be(PlatformInvoiceStatus.Settled);

        // والفاتورةُ نفسها لم تُمَسّ: مبلغُها وأسطرُها كما صدرت.
        invoice.Total.Amount.Should().Be(100m);
        invoice.Lines.Should().HaveCount(1);
    }

    [Fact]
    public void إشعار_دائن_يتجاوز_المتبقّي_مرفوض_ولا_يأخذ_رقماً()
    {
        var invoice = Issued(100m);
        invoice.RecordPayment(Jd(70), PlatformPaymentMethod.BankTransfer, Now, 3, null, null);

        var note = new CreditNote(invoice);
        note.AddLine("تصحيح", 1, Jd(50));   // المتبقّي 30 فقط

        var issue = () => note.Issue(invoice, "CN000001", Now, "سبب", Jd(0), null);
        issue.Should().Throw<InvalidCreditNoteException>();

        // **والرقمُ لم يُثبَّت**: مستندٌ لم يصدر لا يحمل رقماً من السلسلة، وإلّا تركت فجوة.
        note.Number.Should().BeNull();
        note.Status.Should().Be(CreditNoteStatus.Draft);
        invoice.CreditedAmount.Should().Be(0m);
    }

    [Fact]
    public void لا_إشعار_دائن_على_مسوّدة_ولا_بلا_سطر_ولا_بلا_سبب()
    {
        var onDraft = () => new CreditNote(Draft());
        onDraft.Should().Throw<InvalidCreditNoteException>();

        var invoice = Issued(100m);

        var empty = new CreditNote(invoice);
        var noLines = () => empty.Issue(invoice, "CN1", Now, "سبب", Jd(0), null);
        noLines.Should().Throw<InvalidCreditNoteException>();

        var note = new CreditNote(invoice);
        note.AddLine("تصحيح", 1, Jd(10));
        var noReason = () => note.Issue(invoice, "CN1", Now, "   ", Jd(0), null);
        noReason.Should().Throw<InvalidCreditNoteException>();
    }

    // معرّفٌ حقيقيّ لكيانٍ لم يُحفَظ. الحارسُ يقارن المعرّفات، والمعرّفُ صفرٌ قبل الحفظ — فمِن
    // غير هذا يبدو كلُّ كيانَين في الذاكرة كياناً واحداً، ويمرّ اختبارٌ لا يفحص شيئاً.
    private static T WithId<T>(T entity, int id) where T : Souq.Domain.Common.Entity
    {
        typeof(Souq.Domain.Common.Entity).GetProperty(nameof(Souq.Domain.Common.Entity.Id))!
            .SetValue(entity, id);
        return entity;
    }

    [Fact]
    public void إشعار_الدائن_لا_يُقيَّد_على_فاتورة_غير_فاتورته()
    {
        var invoice = WithId(Issued(100m), 1);
        var other = WithId(Issued(50m), 2);

        var note = new CreditNote(invoice);
        note.AddLine("تصحيح", 1, Jd(10));

        var wrongInvoice = () => note.Issue(other, "CN1", Now, "سبب", Jd(0), null);
        wrongInvoice.Should().Throw<InvalidCreditNoteException>();
        other.CreditedAmount.Should().Be(0m);
    }

    // ── العملة ────────────────────────────────────────────────────────────────

    [Fact]
    public void سطر_أو_سداد_بعملة_تخالف_الفاتورة_مرفوض()
    {
        var invoice = new PlatformInvoice(7, Jod, Start, End);
        var foreignLine = () => invoice.AddLine("سطر", 1, new Money(10, "USD"));
        foreignLine.Should().Throw<InvalidPlatformInvoiceException>();

        var issued = Issued(100m);
        var foreignPayment = () => issued.RecordPayment(
            new Money(10, "USD"), PlatformPaymentMethod.Cash, Now, 3, null, null);
        foreignPayment.Should().Throw<InvalidPlatformInvoiceException>();
    }

    [Fact]
    public void مجموع_السطر_يُقرَّب_مرّة_واحدة_بخانات_عملته()
    {
        // كمّيةٌ كسرية × سعر تُنتج أدقَّ من فلس؛ التقريبُ في `Money.FromCalculation` وحدها،
        // والدينارُ ثلاثُ خانات (ADR-0014).
        var invoice = new PlatformInvoice(7, Jod, Start, End);
        var line = invoice.AddLine("ساعاتُ تخزين", 3.3333m, Jd(1));

        line.LineTotal.Amount.Should().Be(3.333m);
        invoice.Subtotal.Amount.Should().Be(3.333m);
    }
}
