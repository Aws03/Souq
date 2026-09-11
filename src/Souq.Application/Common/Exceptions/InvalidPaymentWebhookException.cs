namespace Souq.Application.Common.Exceptions;

// إشعار دفع (Webhook) بتوقيع غير صالح — قد يكون طلباً مزيّفاً. يرميه محوّل البوّابة،
// ويترجمه أمر المعالجة إلى رفض (400) دون لمس أي طلب.
public sealed class InvalidPaymentWebhookException : Exception
{
    public InvalidPaymentWebhookException(Exception? inner = null)
        : base("توقيع إشعار الدفع غير صالح", inner) { }
}
