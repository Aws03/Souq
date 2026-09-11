namespace Souq.Application.Common.Exceptions;

// حالة استخدام تحتاج هوية وُصلت بلا مستخدم مُصادَق (نقطة نُسي عليها [Authorize]).
// الـ API يترجمها إلى 401 — الفشل آمن: لا بيانات تُعاد ولا تُكتب باسم مجهول.
public sealed class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException() : base("يلزم تسجيل الدخول لتنفيذ هذا الإجراء.") { }
}
