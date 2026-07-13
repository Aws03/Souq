using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// إعدادات SMTP جيميل. AppPassword سرّ دوماً (متغيّر بيئة Gmail__AppPassword) —
// لا يُقرأ من appsettings المرفوع أبداً (انظر AddInfrastructure).
// Port: 587 (STARTTLS) قياسياً، أو 465 (TLS ضمني) حين يحجب مزوّد الإنترنت 587.
public class GmailSmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "aws.03.dev@gmail.com";
    public string AppPassword { get; set; } = "";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
}

// ============================================================================
// GmailEmailService — إرسال حقيقي عبر MailKit (المكتبة التي توصي بها Microsoft
// رسمياً بدل System.Net.Mail.SmtpClient المُهمل، والذي لا يدعم TLS الضمني على
// 465 أصلاً — ضروري حين يكون 587 محجوباً). يعمل فقط حين Gmail:AppPassword
// مضبوط؛ غيابه يُبقي النظام يعمل عبر ConsoleEmailService بدل رمي خطأ عند
// الإقلاع (نفس نمط الدفع التجريبي).
// ============================================================================
public class GmailEmailService : IEmailService
{
    private readonly GmailSmtpOptions _opts;
    private readonly ILogger<GmailEmailService> _logger;

    public GmailEmailService(IOptions<GmailSmtpOptions> opts, ILogger<GmailEmailService> logger)
    {
        _opts = opts.Value; _logger = logger;
    }

    public Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default)
    {
        var subject = $"تأكيد الطلب #{orderId} — ماركة";
        return SendAsync(toEmail, subject, EmailTemplates.OrderConfirmation(orderId), ct);
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        var link = $"{_opts.FrontendUrl.TrimEnd('/')}/reset-password?token={resetToken}";
        return SendAsync(toEmail, "إعادة تعيين كلمة المرور — ماركة", EmailTemplates.PasswordReset(link), ct);
    }

    private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("ماركة Marka", _opts.Username));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        // 465 = TLS ضمني (مصافحة فور الاتصال)، غيره = STARTTLS (ترقية بعد الاتصال).
        var socketOptions = _opts.Port == 465 ? SecureSocketOptions.SslOnConnect
            : _opts.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        // سجلّ محاولة صريح قبل الإرسال: المستقبِل + الموضوع + الخادم/المنفذ
        // + المرسِل. بوجوده نعرف من السجل أن الإرسال بدأ فعلاً (لا استُبدل بصمت
        // بـ ConsoleEmailService) وإلى أي عنوان بالضبط — نقطة البداية في تشخيص
        // أي شكوى "لم يصلني البريد".
        _logger.LogInformation(
            "إرسال بريد Gmail: المستقبِل={Email} الموضوع=\"{Subject}\" المرسِل={From} الخادم={Host}:{Port}",
            toEmail, subject, _opts.Username, _opts.Host, _opts.Port);

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions, ct);
            await client.AuthenticateAsync(_opts.Username, _opts.AppPassword, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);
            // تأكيد نجاح صريح: بدونه لا سبيل للتفريق بين "أُرسل فعلاً" و"فشل".
            _logger.LogInformation("✅ نجح إرسال بريد \"{Subject}\" إلى {Email} عبر {Host}:{Port}",
                subject, toEmail, _opts.Host, _opts.Port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // فشل الإرسال لا يجب أن يُسقط تدفّق العمل (مثلاً: لا نكشف فشل جيميل
            // لطالب إعادة التعيين — الرسالة الموحّدة "نجاح دائماً" تبقى كما هي).
            // نسجّل الاستثناء كاملاً (النوع + الرسالة + المكدّس) ليتتبّعه المطوّر.
            // (MailKit يرمي أنواعاً عدة: مصادقة/أوامر SMTP/شبكة — نلتقطها جميعاً
            // عدا الإلغاء الذي يخصّ المستدعي.)
            _logger.LogError(ex,
                "❌ فشل إرسال بريد عبر Gmail SMTP إلى {Email} (الموضوع=\"{Subject}\" الخادم={Host}:{Port}): {ErrorType}: {ErrorMessage}",
                toEmail, subject, _opts.Host, _opts.Port, ex.GetType().Name, ex.Message);
        }
    }
}
