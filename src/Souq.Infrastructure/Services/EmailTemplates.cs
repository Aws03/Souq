namespace Souq.Infrastructure.Services;

// قوالب HTML مشتركة بين كل تنفيذات IEmailService (Resend/Gmail) — مكان واحد
// لتصميم رسائل البريد كي لا يتكرّر HTML نفسه في كل تنفيذ جديد.
internal static class EmailTemplates
{
    public static string OrderConfirmation(int orderId) => $@"<div style=""font-family:Tajawal,Arial,sans-serif;text-align:right;padding:16px;"">
            <p>تم استلام طلبك رقم <b>#{orderId}</b> بنجاح. شكراً لتسوّقك من ماركة.</p>
        </div>";

    public static string PasswordReset(string resetLink) => $@"
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
          <h2 style=""margin:0 0 12px;color:#0F3B3A;"">إعادة تعيين كلمة المرور</h2>
          <p style=""margin:0 0 24px;line-height:1.7;"">
            وصلنا طلب لإعادة تعيين كلمة مرور حسابك في ماركة. اضغط الزر أدناه
            لاختيار كلمة مرور جديدة. هذا الرابط صالح لمدة ساعتين فقط.
          </p>
          <div style=""text-align:center;margin:28px 0;"">
            <a href=""{resetLink}""
               style=""background:#E8A33D;color:#0a2c2b;padding:14px 32px;border-radius:8px;
                      text-decoration:none;font-weight:700;display:inline-block;"">
              إعادة تعيين كلمة المرور
            </a>
          </div>
          <p style=""margin:0;color:#6b736f;font-size:13px;line-height:1.7;"">
            إن لم تطلب هذا، تجاهل هذه الرسالة — لن يتغيّر شيء في حسابك.
          </p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
}
