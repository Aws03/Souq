namespace Souq.Domain.Exceptions;

// أخطاء وحدة Billing (C1، ADR-0047). لكلٍّ رمزه الثابت: الواجهة تتفرّع على الرمز لا على الرسالة (ADR-0017).

// خطأ: بيانات خطة أو انتقال حالة غير صالح (معرّف، اسم، استحقاق غير معروف، تعديل بعد النشر).
public class InvalidPlanException : DomainException
{
    public InvalidPlanException(string message) : base("InvalidPlan", message) { }
}

// خطأ: عملية اشتراك غير صالحة (اشتراك على خطة غير منشورة، اشتراك بلا متجر).
public class InvalidSubscriptionException : DomainException
{
    public InvalidSubscriptionException(string message) : base("InvalidSubscription", message) { }
}

// خطأ: استثناء استحقاق غير صالح (بلا انتهاء، أطول من السقف، بلا سبب أو بلا نسبة).
public class InvalidEntitlementOverrideException : DomainException
{
    public InvalidEntitlementOverrideException(string message) : base("InvalidEntitlementOverride", message) { }
}

// ── فوترةُ التاجر (C5، ADR-0056) ─────────────────────────────────────────────

// خطأ: إعدادُ فوترةِ المنصّة ناقصٌ أو غيرُ صالح — عملةٌ غير مضبوطة، أو مُصدِرٌ بلا اسم، أو مهلةُ
// سدادٍ سالبة. وأكثرُ ما يُرمى به: محاولةُ إصدار فاتورةٍ قبل أن تُضبَط العملة (قرار المالك C-15).
public class InvalidPlatformBillingSettingsException : DomainException
{
    public InvalidPlatformBillingSettingsException(string message)
        : base("InvalidPlatformBillingSettings", message) { }
}

// خطأ: عمليةٌ غير صالحة على فاتورةِ منصّة — تعديلُ مُصدَرةٍ، أو إصدارُ فاتورةٍ بلا سطر، أو تسجيلُ
// سدادٍ يتجاوز المتبقّي، أو مبلغُ ضريبةٍ لا تُنتجه أسطرُ لقطتِه.
public class InvalidPlatformInvoiceException : DomainException
{
    public InvalidPlatformInvoiceException(string message) : base("InvalidPlatformInvoice", message) { }
}

// خطأ: إشعارُ دائنٍ غير صالح — بلا فاتورةٍ مُصدَرة، أو بمبلغٍ يتجاوز ما بقي من فاتورته، أو بعملةٍ
// تخالف عملتَها.
public class InvalidCreditNoteException : DomainException
{
    public InvalidCreditNoteException(string message) : base("InvalidCreditNote", message) { }
}

// خطأ: عمليةٌ غير صالحة على فترةِ فوترة أو حدثٍ قابلٍ للفوترة — إغلاقُ مغلقةٍ، أو إلحاقُ حدثٍ
// بفترةٍ أُغلقت، أو مدّةٌ تنتهي قبل أن تبدأ.
public class InvalidBillingPeriodException : DomainException
{
    public InvalidBillingPeriodException(string message) : base("InvalidBillingPeriod", message) { }
}
