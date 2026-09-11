namespace Souq.Application.Common.Interfaces;

// منفذ البريد. روابط إعادة التعيين والتأكيد تصل جاهزة من IStorefrontLinks (مضيف المتجر الذي جاء
// منه الطلب) — لا عنوان واجهة واحد لكل المتاجر يرسل عميل المتجر B إلى متجر A.
public interface IEmailService
{
    Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default);

    Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct = default);

    Task SendEmailVerificationAsync(string toEmail, string verificationLink, CancellationToken ct = default);

    // دعوة حساب إدارة (مدير/موظّف متجر أو حساب منصّة) لاختيار كلمة مروره — storeName لنصّ الرسالة.
    Task SendInvitationAsync(string toEmail, string storeName, string invitationLink, CancellationToken ct = default);
}

// ============================================================================
// IStorefrontLinks — روابط تُرسَل بالبريد إلى صفحات الواجهة، على مضيف المتجر (أو المنصّة) الذي جاء
// منه الطلب. آمن من "تسميم رابط إعادة التعيين" بترويسة Host مزيّفة: مضيف غير مسجَّل لمتجر يُرفض بـ
// 404 قبل أن تصل أي حالة استخدام (TenantResolutionMiddleware). التنفيذ في طبقة الـ API.
// ============================================================================
public interface IStorefrontLinks
{
    string PasswordReset(string token);

    string EmailVerification(string token);

    // host: مضيف صفحة القبول — نطاق المتجر حين تدعو المنصّة مديره (الطلب على مضيف المنصّة)؛ null ⇒ مضيف
    // الطلب نفسه (موظّف يدعوه مدير متجره، أو حساب منصّة يدعوه المالك).
    string Invitation(string token, string? host = null);
}
