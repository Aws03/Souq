using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Souq.Application.Common.Notifications;

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
}

// ============================================================================
// GmailEmailService — إرسال حقيقي عبر MailKit (توصية Microsoft بدل SmtpClient المُهمل، ويدعم TLS الضمني على 465). اسم
// المرسِل اسم المتجر والردّ لبريد تواصله (المرحلة 14). الفشل يرمي EmailDeliveryException فيُعاد من صندوق الصادر. السجل منقَّح:
// المستلم والمرسِل مُقنَّعان.
// ============================================================================
public class GmailEmailService : IEmailSender
{
    private readonly GmailSmtpOptions _opts;
    private readonly ILogger<GmailEmailService> _logger;

    public GmailEmailService(IOptions<GmailSmtpOptions> opts, ILogger<GmailEmailService> logger)
    {
        _opts = opts.Value; _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var recipient = LogRedaction.MaskEmail(message.To);
        if (string.IsNullOrWhiteSpace(_opts.Username))
            throw new EmailDeliveryException("Gmail مضبوط بلا عنوان مرسِل (Gmail:Username)");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(EmailSenders.CleanName(message.FromName, "Souq"), _opts.Username));
        mime.To.Add(MailboxAddress.Parse(message.To));
        if (message.ReplyTo is not null) mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        // 465 = TLS ضمني (مصافحة فور الاتصال)، غيره = STARTTLS (ترقية بعد الاتصال).
        var socketOptions = _opts.Port == 465 ? SecureSocketOptions.SslOnConnect
            : _opts.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions, ct);
            await client.AuthenticateAsync(_opts.Username, _opts.AppPassword, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);
            _logger.LogInformation("Email {Kind} sent to {Recipient} via {Host}:{Port}", message.Kind, recipient, _opts.Host, _opts.Port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("SMTP delivery of email {Kind} to {Recipient} via {Host}:{Port} (sender {Sender}) failed: {ErrorType}",
                message.Kind, recipient, _opts.Host, _opts.Port, LogRedaction.MaskEmail(_opts.Username), ex.GetType().Name);
            throw new EmailDeliveryException("تعذّر الإرسال عبر SMTP", ex);
        }
    }
}
