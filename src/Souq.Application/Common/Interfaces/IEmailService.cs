namespace Souq.Application.Common.Interfaces;

// منفذ البريد. روابط إعادة التعيين والتأكيد تصل جاهزة من IStorefrontLinks (مضيف المتجر الذي جاء
// منه الطلب) — لا عنوان واجهة واحد لكل المتاجر يرسل عميل المتجر B إلى متجر A.
public interface IEmailService
{
    Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default);

    Task SendPasswordResetEmailAsync(string toEmail, string resetLink, CancellationToken ct = default);

    Task SendEmailVerificationAsync(string toEmail, string verificationLink, CancellationToken ct = default);
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
}
