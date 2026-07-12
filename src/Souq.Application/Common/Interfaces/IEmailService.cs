namespace Souq.Application.Common.Interfaces;
public interface IEmailService
{
    Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default);

    // نمرّر الرمز الخام لا الرابط الكامل: بناء الرابط (يحتاج FrontendUrl، تفصيل
    // بيئة/نشر) هو مسؤولية Infrastructure، لا Application (انظر ForgotPasswordHandler).
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default);
}
