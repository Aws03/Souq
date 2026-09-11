using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// إعدادات SMTP جيميل. AppPassword سرّ دوماً (Gmail__AppPassword). Username (عنوان المرسِل)
// من الإعداد فقط — لا عنوان شخصي مكتوب في الكود (Phase 0 B12).
// Port: 587 (STARTTLS) قياسياً، أو 465 (TLS ضمني) حين يحجب مزوّد الإنترنت 587.
public class GmailSmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string AppPassword { get; set; } = "";
    public string FrontendUrl { get; set; } = "http://localhost:5173";
}

// ============================================================================
// GmailEmailService — إرسال حقيقي عبر MailKit (توصية Microsoft بدل SmtpClient المُهمل،
// ويدعم TLS الضمني على 465). السجل منقَّح: المستلم والمرسِل مُقنَّعان.
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
        => SendAsync(toEmail, $"تأكيد الطلب #{orderId} — ماركة", EmailTemplates.OrderConfirmation(orderId), ct);

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        var link = $"{_opts.FrontendUrl.TrimEnd('/')}/reset-password?token={resetToken}";
        return SendAsync(toEmail, "إعادة تعيين كلمة المرور — ماركة", EmailTemplates.PasswordReset(link), ct);
    }

    private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var recipient = LogRedaction.MaskEmail(toEmail);
        if (string.IsNullOrWhiteSpace(_opts.Username))
        {
            _logger.LogError("Gmail مضبوط بلا عنوان مرسِل (Gmail:Username) — لم يُرسَل \"{Subject}\" إلى {Recipient}",
                subject, recipient);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("ماركة Marka", _opts.Username));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        // 465 = TLS ضمني (مصافحة فور الاتصال)، غيره = STARTTLS (ترقية بعد الاتصال).
        var socketOptions = _opts.Port == 465 ? SecureSocketOptions.SslOnConnect
            : _opts.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions, ct);
            await client.AuthenticateAsync(_opts.Username, _opts.AppPassword, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);
            _logger.LogInformation("أُرسل بريد \"{Subject}\" إلى {Recipient} عبر {Host}:{Port}",
                subject, recipient, _opts.Host, _opts.Port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // فشل الإرسال لا يُسقط تدفّق العمل. نسجّل الاستثناء كاملاً (بلا كلمة المرور).
            _logger.LogError(ex, "فشل إرسال بريد \"{Subject}\" إلى {Recipient} عبر {Host}:{Port} (المرسِل {Sender})",
                subject, recipient, _opts.Host, _opts.Port, LogRedaction.MaskEmail(_opts.Username));
        }
    }
}
