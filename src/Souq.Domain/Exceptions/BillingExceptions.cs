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
