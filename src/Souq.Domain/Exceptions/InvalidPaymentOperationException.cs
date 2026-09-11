namespace Souq.Domain.Exceptions;

// قاعدة دفع أو استرداد مخالَفة (المرحلة 11): استرداد يتجاوز المدفوع، استرداد دفعة لم تنجح، مفاتيح بوّابة بصيغة خاطئة.
// الرمز الافتراضي عام؛ الحالات التي تحتاج الواجهة تمييزها تمرّر رمزها (RefundExceedsPayment...).
public class InvalidPaymentOperationException : DomainException
{
    public InvalidPaymentOperationException(string message, string code = "InvalidPaymentOperation") : base(code, message) { }
}
