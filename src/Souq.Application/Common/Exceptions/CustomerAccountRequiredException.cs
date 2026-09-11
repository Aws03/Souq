namespace Souq.Application.Common.Exceptions;

// حساب مُصادَق بلا ملف عميل في هذا المتجر (موظّف، مدير) يحاول الشراء أو التقييم. الـ API يترجمها إلى
// 403 CustomerAccountRequired — الهوية صحيحة لكن هذا الإجراء لحساب عميل.
public sealed class CustomerAccountRequiredException : Exception
{
    public CustomerAccountRequiredException() : base("هذا الإجراء لحسابات العملاء. سجّل الدخول بحساب عميل.") { }
}
