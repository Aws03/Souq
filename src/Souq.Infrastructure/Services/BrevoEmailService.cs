using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Notifications;

namespace Souq.Infrastructure.Services;

// ApiKey سرّ دوماً (Brevo__ApiKey). SenderEmail يأتي من الإعداد (Brevo:SenderEmail أو عنوان المرسِل المشترك Gmail:Username) —
// لا عنوان شخصي مكتوب في الكود (Phase 0 B12). اسم المرسِل اسم المتجر لكل رسالة (المرحلة 14).
public class BrevoOptions
{
    public string ApiKey { get; set; } = "";
    public string SenderName { get; set; } = "Souq";
    public string SenderEmail { get; set; } = "";
}

// ============================================================================
// BrevoEmailService — بديل عبر Brevo API. المصادقة برأس "api-key" (لا Bearer). نفس سياسة السجل المنقَّح وعميل HTTP المُدار
// بمهلة في ResendEmailService، والفشل يرمي EmailDeliveryException فيُعاد من صندوق الصادر.
// ============================================================================
public class BrevoEmailService : IEmailSender
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly BrevoOptions _opts;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(HttpClient http, IOptions<BrevoOptions> opts, ILogger<BrevoEmailService> logger)
    {
        _http = http; _opts = opts.Value; _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var recipient = LogRedaction.MaskEmail(message.To);
        if (string.IsNullOrWhiteSpace(_opts.SenderEmail))
            throw new EmailDeliveryException("Brevo مضبوط بلا عنوان مرسِل (Brevo:SenderEmail)");

        var payload = JsonSerializer.Serialize(new
        {
            sender = new { name = EmailSenders.CleanName(message.FromName, _opts.SenderName), email = _opts.SenderEmail },
            to = new[] { new { email = message.To } },
            replyTo = message.ReplyTo is null ? null : new { email = message.ReplyTo },
            subject = message.Subject,
            htmlContent = message.HtmlBody,
            textContent = message.TextBody,
        }, Json);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("api-key", _opts.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new EmailDeliveryException("تعذّر الاتصال بـ Brevo", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email {Kind} sent to {Recipient} via Brevo ({StatusCode})",
                    message.Kind, recipient, (int)response.StatusCode);
                return;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Brevo rejected email {Kind} to {Recipient}: {StatusCode} — {ProviderError}",
                message.Kind, recipient, (int)response.StatusCode, LogRedaction.Truncate(LogRedaction.MaskEmails(errorBody)));
            throw new EmailDeliveryException($"Brevo أعاد الحالة {(int)response.StatusCode}");
        }
    }
}
