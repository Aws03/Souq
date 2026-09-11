using System.Net;

namespace Souq.Infrastructure.Services;

// قوالب HTML مشتركة بين كل تنفيذات IEmailService (Resend/Brevo/Gmail) — مكان واحد لتصميم رسائل
// البريد. قوالب لكل متجر وبهويّته في المرحلة 14 (المرحلة الحالية: هوية الواجهة الافتراضية).
internal static class EmailTemplates
{
    public static string OrderConfirmation(int orderId) => $@"<div style=""font-family:Tajawal,Arial,sans-serif;text-align:right;padding:16px;"">
            <p>تم استلام طلبك رقم <b>#{orderId}</b> بنجاح. شكراً لتسوّقك من ماركة.</p>
        </div>";

    public static string PasswordReset(string resetLink) => ActionEmail(
        "إعادة تعيين كلمة المرور",
        "وصلنا طلب لإعادة تعيين كلمة مرور حسابك. اضغط الزر أدناه لاختيار كلمة مرور جديدة. هذا الرابط صالح لمدة ساعتين فقط.",
        "إعادة تعيين كلمة المرور", resetLink,
        "إن لم تطلب هذا، تجاهل هذه الرسالة — لن يتغيّر شيء في حسابك.");

    public static string EmailVerification(string verificationLink) => ActionEmail(
        "تأكيد بريدك الإلكتروني",
        "مرحباً بك! اضغط الزر أدناه لتأكيد أن هذا البريد لك. الرابط صالح لمدة 48 ساعة.",
        "تأكيد البريد", verificationLink,
        "إن لم تنشئ حساباً، تجاهل هذه الرسالة.");

    // الرابط يُرمَّز لسمة HTML (يحمل & في سلسلة الاستعلام) — لا حقن في القالب من أي قيمة.
    private static string ActionEmail(string title, string body, string action, string link, string footer) => $@"
<!DOCTYPE html>
<html lang=""ar"" dir=""rtl"">
<body style=""margin:0;padding:0;background:#F0EBE1;font-family:Tajawal,Arial,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#F0EBE1;padding:32px 0;"">
    <tr><td align=""center"">
      <table width=""480"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff;border-radius:14px;overflow:hidden;"">
        <tr><td style=""background:#0F3B3A;padding:24px;text-align:center;"">
          <span style=""font-size:22px;font-weight:700;color:#FAF7F1;"">Mar<span style=""color:#E8A33D;"">ka</span></span>
        </td></tr>
        <tr><td style=""padding:32px;text-align:right;color:#1A2421;"">
          <h2 style=""margin:0 0 12px;color:#0F3B3A;"">{WebUtility.HtmlEncode(title)}</h2>
          <p style=""margin:0 0 24px;line-height:1.7;"">{WebUtility.HtmlEncode(body)}</p>
          <div style=""text-align:center;margin:28px 0;"">
            <a href=""{WebUtility.HtmlEncode(link)}""
               style=""background:#E8A33D;color:#0a2c2b;padding:14px 32px;border-radius:8px;
                      text-decoration:none;font-weight:700;display:inline-block;"">
              {WebUtility.HtmlEncode(action)}
            </a>
          </div>
          <p style=""margin:0;color:#6b736f;font-size:13px;line-height:1.7;"">{WebUtility.HtmlEncode(footer)}</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
}
