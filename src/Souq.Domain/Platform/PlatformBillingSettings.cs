using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Platform;

// ============================================================================
// إعدادُ فوترةِ المنصّة: **صفٌّ واحد عالميّ** (الشكل C في ADR-0047 §1) يحمل ما تحتاجه سوق كي
// تُصدِر فاتورةً لتاجر — العملةُ، واسمُ المُصدِر وعنوانُه ورقمُه الضريبيّ، ومهلةُ السداد، وتعليماتُ
// الدفع، واختيارُ ملفّ الضريبة الذي تُفوتر سوق تحته ([ADR-0056](0056)).
//
// ============================================================================
// **لماذا العملةُ قيمةٌ في القاعدة لا ثابتٌ في الشيفرة؟**
//
// قرار المالك `C-15` = A يقول: فواتيرُ اشتراكات التجّار بالدينار الأردني. وهذا **جوابٌ تجاريّ
// يُدخَل**، لا سطرٌ يُكتب: قاعدةُ الواجهة البيضاء (AGENTS §3 القاعدة 17) تمنع كتابة رمز عملةٍ في
// شيفرة المنتج أو في إعداده المرفوع، ويحرسها `WhiteLabelSourceTests` فعلاً. فلو كُتب الرمز هنا
// لسقط البناء — وهذا هو التصميم لا عائقُه: المنصّة التي تُباع لمشغّلٍ في سوقٍ آخر تُدخل عملتَها
// ولا تنتظر إصداراً.
//
// **والنتيجةُ أنّ الإعداد يفشل مغلقاً**: بلا عملةٍ مضبوطة وبلا اسمِ مُصدِر **لا تُصدَر فاتورةٌ
// واحدة**. وهو النمطُ نفسه الذي يحكم الضريبة في ADR-0055: قيمةٌ في القاعدة ليست قيمةً يُعتمد
// عليها حتى يضعها إنسانٌ بعلمه.
// ============================================================================
//
// **ولماذا لسوق ملفُّ ضريبةٍ خاصٌّ بها؟** لأنّ المُصدِر في هذه الفاتورة هو سوق لا المتجر: ضريبةُ
// فاتورةِ الاشتراك تتبع اختصاصَ سوق نفسها، لا اختصاصَ التاجر ولا اختصاص مشترِيه. فالاختياران
// منفصلان تماماً — `StoreTaxSettings` للمتجر و**هذا** للمنصّة — ولو جُمعا لصار تغييرُ أحدهما
// يُحرّك الآخر بلا أن يقصده أحد.
// ============================================================================
public class PlatformBillingSettings : Entity
{
    public const int IssuerNameMaxLength = 200;
    public const int IssuerAddressMaxLength = 500;
    public const int TaxNumberMaxLength = 60;
    public const int NumberPrefixMaxLength = 10;
    public const int PaymentInstructionsMaxLength = 2000;
    public const int MaxPaymentTermsDays = 365;
    public const int MaxGracePeriodDays = 365;
    public const int MaxReminderIntervalDays = 90;
    public const int MaxReminders = 20;

    // البادئتان الافتراضيّتان حرفان لاتينيّان لا علامةٌ تجارية: `INV` و`CN` مصطلحا مستنداتٍ
    // محاسبيّان، والمشغّل يبدّلهما من شاشته. ولذلك لا يمسّهما اختبارُ الواجهة البيضاء.
    public const string DefaultInvoicePrefix = "INV";
    public const string DefaultCreditNotePrefix = "CN";

    // ثلاثون يوماً افتراضاً لمهلة السداد وسبعةٌ للسماح. **ليستا قاعدةَ قانون ولا شرطاً تعاقدياً**:
    // هما قيمتان يبدأ منهما المشغّل ويغيّرهما من شاشته، ووجودُ افتراضٍ هنا أفضلُ من فاتورةٍ
    // تُصدَر بمهلةٍ صفر لأنّ أحداً لم يملأ حقلاً.
    public const int DefaultPaymentTermsDays = 30;
    public const int DefaultGracePeriodDays = 7;

    // سبعةُ أيام بين تذكيرٍ وآخر، وثلاثةُ تذكيرات. قيمتان يبدأ منهما المشغّل ويغيّرهما — ولا
    // أثرَ لهما ما لم تُفعَّل المطالبة أصلاً.
    public const int DefaultReminderIntervalDays = 7;
    public const int DefaultMaxReminders = 3;

    // ============================================================================
    // عملةُ فواتير المنصّة. `null` ⇒ **لم تُضبَط بعد** ⇒ لا إصدار. وهي عملةُ سوق لا عملةَ المتجر:
    // متجرٌ يبيع بعملةٍ أخرى يبقى يبيع بها، وفاتورةُ اشتراكه تأتي بعملة المنصّة — وهذا بالضبط ما
    // يعنيه `C-15`.
    // ============================================================================
    public string? Currency { get; private set; }

    public string? IssuerName { get; private set; }
    public string? IssuerAddress { get; private set; }
    public string? IssuerTaxNumber { get; private set; }

    public string InvoiceNumberPrefix { get; private set; } = DefaultInvoicePrefix;
    public string CreditNoteNumberPrefix { get; private set; } = DefaultCreditNotePrefix;

    public int PaymentTermsDays { get; private set; } = DefaultPaymentTermsDays;

    // مهلةُ السماح بعد الاستحقاق قبل أن تُعَدّ الفاتورة متأخّرةً تأخّراً يُتصرَّف فيه. تُقرأ هنا
    // ولا يُتصرَّف بها بعد: **المطالبةُ والتعليقُ الآليّان هما C6**، وهذه القيمةُ مدخلُها.
    public int GracePeriodDays { get; private set; } = DefaultGracePeriodDays;

    // تعليماتُ الدفع كما تُطبع على الفاتورة: رقمُ حسابٍ بنكيّ، أو IBAN، أو ما يقوله المشغّل.
    // نصٌّ لا يقرؤه الكود — التحصيلُ يدويٌّ بقرار `C-15`، فهذا هو كلُّ «بوّابة الدفع» في C5.
    public string? PaymentInstructions { get; private set; }

    // اختيارُ سوق لملفّ اختصاصها الضريبيّ. `null` ⇒ لا ضريبةَ على فواتير الاشتراك، ويُقال ذلك
    // بسببه لا صمتاً (كما في `StoreTaxSettings`).
    public int? TaxProfileId { get; private set; }
    public bool TaxCollectionEnabled { get; private set; }

    // ============================================================================
    // **المطالبة الآلية: معطّلةٌ حتى يُفعّلها إنسان** (C6، [ADR-0058](0058)).
    //
    // وهذا ليس حذراً زائداً بل الافتراضَ الوحيد الصحيح: هذه أوّلُ قدرةٍ في المنتج تتّخذ إجراءً
    // **لا رجعةَ فيه ضدّ عميلٍ يدفع** بلا إنسانٍ في الحلقة. فنشرُ هذه الشريحة على منصّةٍ قائمة
    // يجب ألّا يُعلّق متجراً واحداً يوم النشر — والمشغّل يُفعّلها حين يكون قد راجع مهلَه وتأكّد
    // أنّ فواتيره تُصدَر في وقتها.
    //
    // ومعها تُعطَّل التذكيراتُ أيضاً: منصّةٌ تُذكّر ولا تُصعّد أبداً تُدرّب تجّارها على تجاهل
    // تذكيراتها.
    // ============================================================================
    public bool DunningEnabled { get; private set; }

    public int ReminderIntervalDays { get; private set; } = DefaultReminderIntervalDays;

    public int MaxRemindersBeforeSuspension { get; private set; } = DefaultMaxReminders;

    private PlatformBillingSettings() { }

    // الصفُّ الأوّل: فارغٌ بلا عملة. **لا يُصدِر شيئاً** حتى يملأه إنسان — وهذا مقصود: بذرٌ يضع
    // عملةً نيابةً عن المشغّل يجعل أوّلَ فاتورةٍ تصدر بعملةٍ لم يقرّرها أحد.
    public static PlatformBillingSettings Empty() => new();

    // ============================================================================
    // البوّابةُ الوحيدة للإصدار. تُقرأ قبل كل إصدار، وسببُ المنع يُرفع باسمه إلى الشاشة: مشغّلٌ
    // يضغط «أصدِر» ولا يحدث شيء يفتح بلاغاً، ومشغّلٌ يقرأ «اضبط عملة الفوترة أولاً» يُكمل إعداده.
    // ============================================================================
    public bool CanIssue => !string.IsNullOrEmpty(Currency) && !string.IsNullOrEmpty(IssuerName);

    public void SetCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            Currency = null;
            return;
        }

        // يمرّ عبر `Money` كي يتحقّق الرمزُ من ISO 4217 ويُوحَّد شكلُه في موضعٍ واحد — ولا يُعاد
        // كتابةُ فحصِ العملات هنا.
        Currency = Money.Zero(currency).Currency;
    }

    public void SetIssuer(string? name, string? address, string? taxNumber)
    {
        IssuerName = Normalize(name, IssuerNameMaxLength, "اسم المُصدِر");
        IssuerAddress = Normalize(address, IssuerAddressMaxLength, "عنوان المُصدِر", allowNewLines: true);
        IssuerTaxNumber = Normalize(taxNumber, TaxNumberMaxLength, "الرقم الضريبيّ للمُصدِر");
    }

    public void SetNumberPrefixes(string? invoicePrefix, string? creditNotePrefix)
    {
        InvoiceNumberPrefix = NormalizePrefix(invoicePrefix, DefaultInvoicePrefix);
        CreditNoteNumberPrefix = NormalizePrefix(creditNotePrefix, DefaultCreditNotePrefix);

        // بادئتان متطابقتان تجعلان رقمَ مستندٍ لا يدلّ على نوعه، وسلسلتان منفصلتان تنتجان عندئذٍ
        // رقمَين متطابقَين لمستندَين مختلفَين — وهو أسوأُ ما يمكن أن يحدث لسلسلةِ ترقيم.
        if (string.Equals(InvoiceNumberPrefix, CreditNoteNumberPrefix, StringComparison.Ordinal))
            throw new InvalidPlatformBillingSettingsException(
                "بادئةُ الفاتورة وبادئةُ إشعار الدائن لا تتطابقان — وإلّا حمل مستندان مختلفان الرقمَ نفسه");
    }

    public void SetTerms(int paymentTermsDays, int gracePeriodDays)
    {
        if (paymentTermsDays is < 0 or > MaxPaymentTermsDays)
            throw new InvalidPlatformBillingSettingsException($"مهلةُ السداد بين 0 و{MaxPaymentTermsDays} يوماً");
        if (gracePeriodDays is < 0 or > MaxGracePeriodDays)
            throw new InvalidPlatformBillingSettingsException($"مهلةُ السماح بين 0 و{MaxGracePeriodDays} يوماً");
        PaymentTermsDays = paymentTermsDays;
        GracePeriodDays = gracePeriodDays;
    }

    // ============================================================================
    // ضبطُ المطالبة. **ولا تُفعَّل بلا مهلةِ سماحٍ موجبة**: مهلةُ صفرٍ مع تفعيلٍ تعني تعليقاً في
    // اليوم التالي للاستحقاق مباشرةً — وهو ما لا يقصده أحد، ولو قصده لكتبه يوماً واحداً.
    // ============================================================================
    public void SetDunning(bool enabled, int reminderIntervalDays, int maxReminders)
    {
        if (reminderIntervalDays is < 1 or > MaxReminderIntervalDays)
            throw new InvalidPlatformBillingSettingsException(
                $"الفاصل بين التذكيرات بين 1 و{MaxReminderIntervalDays} يوماً");
        if (maxReminders is < 0 or > MaxReminders)
            throw new InvalidPlatformBillingSettingsException($"عددُ التذكيرات بين 0 و{MaxReminders}");
        if (enabled && GracePeriodDays < 1)
            throw new InvalidPlatformBillingSettingsException(
                "لا تُفعَّل المطالبة بمهلة سماحٍ صفر — التعليقُ عندها يقع في اليوم التالي للاستحقاق");

        DunningEnabled = enabled;
        ReminderIntervalDays = reminderIntervalDays;
        MaxRemindersBeforeSuspension = maxReminders;
    }

    public void SetPaymentInstructions(string? instructions) =>
        PaymentInstructions = Normalize(instructions, PaymentInstructionsMaxLength, "تعليمات الدفع", allowNewLines: true);

    // اختيارُ ملفّ الضريبة. وإلغاءُ الاختيار يُطفئ الجمعَ معه: حالةٌ تقول «أجمع» بلا ملفٍّ تدّعي
    // ما لا تفعل — القاعدةُ نفسها التي يفرضها `StoreTaxSettings`.
    public void SelectTaxProfile(int? taxProfileId, bool collectionEnabled)
    {
        if (taxProfileId is <= 0)
            throw new InvalidPlatformBillingSettingsException("معرّفُ ملفّ الضريبة غير صالح");
        TaxProfileId = taxProfileId;
        TaxCollectionEnabled = taxProfileId is not null && collectionEnabled;
    }

    private static string NormalizePrefix(string? value, string fallback)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(trimmed)) return fallback;
        if (trimmed.Length > NumberPrefixMaxLength)
            throw new InvalidPlatformBillingSettingsException($"بادئةُ الترقيم حتى {NumberPrefixMaxLength} محارف");
        if (!trimmed.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '-'))
            throw new InvalidPlatformBillingSettingsException("بادئةُ الترقيم حروفٌ لاتينية وأرقامٌ وشرطة فقط");
        return trimmed;
    }

    private static string? Normalize(string? value, int maxLength, string field, bool allowNewLines = false)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            throw new InvalidPlatformBillingSettingsException($"{field} حتى {maxLength} حرفاً");
        if (trimmed.Any(c => char.IsControl(c) && !(allowNewLines && c is '\n' or '\r')))
            throw new InvalidPlatformBillingSettingsException($"{field} يحتوي محارف غير مسموحة");
        return trimmed;
    }
}
