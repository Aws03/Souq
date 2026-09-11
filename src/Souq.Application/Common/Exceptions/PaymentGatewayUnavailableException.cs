namespace Souq.Application.Common.Exceptions;

// حساب البوّابة المطلوب لا يعمل الآن (المرحلة 11): سرّ متجر لا يُفكّ، أو دفعة أخذها حساب متجر أُزيل. 503 — لا رجوع صامت
// إلى حساب النشر: ذلك يقبض مال متجر في حساب غيره أو يستردّ من حساب لم يقبض.
public sealed class PaymentGatewayUnavailableException : Exception
{
    public PaymentGatewayUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
