namespace Souq.Domain.Exceptions;

// أخطاء وحدة الضريبة (C9's sibling phase، ADR-0055). لكلٍّ رمزه الثابت: الواجهة تتفرّع على الرمز
// لا على الرسالة (ADR-0017).

// خطأ: بيانات ملفّ ضريبةٍ أو إصدارٍ غير صالحة — رمزُ اختصاص، اسم، نقاط أساس خارج المدى، تعديلٌ
// بعد النشر، تحقّقٌ من مسوّدة، أو تاريخُ نفاذٍ يسبق آخر منشور.
public class InvalidTaxProfileException : DomainException
{
    public InvalidTaxProfileException(string message) : base("InvalidTaxProfile", message) { }
}

// خطأ: إعدادُ ضريبةِ متجرٍ غير صالح — تفعيلُ الجمع بلا ملفّ مختار، أو رقمُ تسجيلٍ أطول من حدّه.
public class InvalidStoreTaxSettingsException : DomainException
{
    public InvalidStoreTaxSettingsException(string message) : base("InvalidStoreTaxSettings", message) { }
}
